#!/usr/bin/env node
// Builds both sides' types from the one change-list schema:
//
//   schema/change-list.schema.json
//     -> server/src/types/change-list.ts      (json-schema-to-typescript)
//     -> addin/Generated/ChangeList.g.cs      (this file's C# writer, [DataContract] classes)
//     -> addin/Generated/change-list.schema.sha256
//
// The add-in's build compares that SHA-256 with the schema file's own and
// fails when they differ, so the C# side cannot compile against a stale copy.
//
//   node scripts/gen-types.ts           write the three files
//   node scripts/gen-types.ts --check   exit 1 if any of them is out of date

import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { compile, type JSONSchema } from 'json-schema-to-typescript';

const ROOT = fileURLToPath(new URL('..', import.meta.url));
const SCHEMA_PATH = `${ROOT}schema/change-list.schema.json`;
const TS_PATH = `${ROOT}server/src/types/change-list.ts`;
const CS_PATH = `${ROOT}addin/Generated/ChangeList.g.cs`;
const SHA_PATH = `${ROOT}addin/Generated/change-list.schema.sha256`;

type Json = Record<string, unknown>;

export function schemaSha256(bytes: Buffer): string {
  return createHash('sha256').update(bytes).digest('hex');
}

export async function typescriptFor(schema: Json, sha: string): Promise<string> {
  const banner = `// GENERATED from schema/change-list.schema.json (sha256 ${sha}) by scripts/gen-types.ts.\n// Do not edit: change the schema and run \`npm run gen\`.`;
  return compile(structuredClone(schema) as JSONSchema, 'ChangeList', {
    bannerComment: banner,
    additionalProperties: false,
    unreachableDefinitions: true,
    strictIndexSignatures: true,
    style: { singleQuote: true, printWidth: 120 },
  });
}

// ---- C# ------------------------------------------------------------------------

function pascal(name: string): string {
  return name
    .replace(/^\$/, '')
    .split(/[_\-\s]+/)
    .filter(Boolean)
    .map((p) => (p[0] as string).toUpperCase() + p.slice(1))
    .join('');
}

function refName(ref: string): string {
  const m = /^#\/\$defs\/(.+)$/.exec(ref);
  if (!m) throw new Error(`unsupported $ref ${ref}`);
  return m[1] as string;
}

interface Prop {
  json: string;
  cs: string;
  type: string;
  required: boolean;
  description?: string;
  constValue?: string | number;
}

class CSharpWriter {
  private readonly schema: Json;
  private readonly defs: Record<string, Json>;
  private readonly out: string[] = [];

  constructor(schema: Json) {
    this.schema = schema;
    this.defs = (schema.$defs ?? {}) as Record<string, Json>;
  }

  private isScalarDef(name: string): boolean {
    const d = this.defs[name];
    return !!d && (d.type === 'string' || d.type === 'number' || d.type === 'integer');
  }

  private scalarType(s: Json, required: boolean): string {
    if (s.type === 'string' || typeof s.const === 'string' || (Array.isArray(s.enum) && typeof s.enum[0] === 'string')) return 'string';
    if (s.type === 'integer' || (typeof s.const === 'number' && Number.isInteger(s.const))) return required ? 'long' : 'long?';
    if (s.type === 'number') return required ? 'double' : 'double?';
    if (s.type === 'boolean') return required ? 'bool' : 'bool?';
    throw new Error(`unsupported scalar ${JSON.stringify(s)}`);
  }

  typeOf(s: Json, required: boolean): string {
    if (typeof s.$ref === 'string') {
      const name = refName(s.$ref);
      if (this.isScalarDef(name)) return this.typeOf(this.defs[name] as Json, required);
      return pascal(name);
    }
    if (s.type === 'array') return `List<${this.typeOf(s.items as Json, true)}>`;
    if (s.type === 'object') throw new Error('inline objects are not supported; put them in $defs');
    return this.scalarType(s, required);
  }

  private propsOf(def: Json, allOptional: boolean): Prop[] {
    const req = new Set((def.required as string[] | undefined) ?? []);
    return Object.entries((def.properties ?? {}) as Record<string, Json>).map(([json, s]) => {
      const required = !allOptional && req.has(json);
      const p: Prop = { json, cs: pascal(json), type: this.typeOf(s, required), required };
      if (typeof s.description === 'string') p.description = s.description;
      if (s.const !== undefined && !allOptional) p.constValue = s.const as string | number;
      return p;
    });
  }

  /** A oneOf of object definitions becomes one class holding every variant's properties, all optional. */
  private unionProps(def: Json): Prop[] {
    const merged = new Map<string, Prop>();
    for (const v of def.oneOf as Json[]) {
      const name = refName(v.$ref as string);
      for (const p of this.propsOf(this.defs[name] as Json, true)) {
        const seen = merged.get(p.json);
        if (seen && seen.type !== p.type) throw new Error(`variants of ${JSON.stringify(def.description)} disagree on ${p.json}`);
        merged.set(p.json, p);
      }
    }
    return [...merged.values()];
  }

  private line(s = ''): void {
    this.out.push(s);
  }

  private doc(text: string | undefined, indent: string): void {
    if (!text) return;
    const esc = text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    this.line(`${indent}/// <summary>${esc}</summary>`);
  }

  private klass(name: string, description: string | undefined, props: Prop[], note?: string): void {
    this.doc(note ? `${description ?? ''} ${note}`.trim() : description, '    ');
    this.line('    [DataContract]');
    this.line(`    public sealed partial class ${name}`);
    this.line('    {');
    props.forEach((p, i) => {
      if (p.constValue !== undefined) {
        const lit = typeof p.constValue === 'string' ? JSON.stringify(p.constValue) : String(p.constValue);
        const t = typeof p.constValue === 'string' ? 'string' : 'long';
        this.line(`        public const ${t} ${p.cs}Value = ${lit};`);
        this.line();
      }
      this.doc(p.description, '        ');
      // A required member always writes its value (a required 0 must not vanish); an optional one is left out when unset.
      const flags = p.required ? 'IsRequired = true, EmitDefaultValue = true' : 'IsRequired = false, EmitDefaultValue = false';
      this.line(`        [DataMember(Name = ${JSON.stringify(p.json)}, ${flags}, Order = ${i})]`);
      this.line(`        public ${p.type} ${p.cs} { get; set; }`);
      if (i < props.length - 1) this.line();
    });
    this.line('    }');
    this.line();
  }

  private enumValues(owner: string, json: string, values: string[]): void {
    this.line(`    /// <summary>The values ${owner}.${pascal(json)} may take.</summary>`);
    this.line(`    public static class ${owner}${pascal(json)}Values`);
    this.line('    {');
    for (const v of values) this.line(`        public const string ${pascal(v)} = ${JSON.stringify(v)};`);
    this.line('    }');
    this.line();
  }

  write(sha: string): string {
    this.line(`// GENERATED from schema/change-list.schema.json (sha256 ${sha}) by scripts/gen-types.ts.`);
    this.line('// Do not edit: change the schema and run `npm run gen`. The build checks the SHA-256 above against the schema.');
    this.line('// <auto-generated />');
    this.line('using System.Collections.Generic;');
    this.line('using System.Runtime.Serialization;');
    this.line();
    this.line('namespace Flo2Rvt.ChangeLists');
    this.line('{');
    this.line('    /// <summary>The schema these types were generated from.</summary>');
    this.line('    public static class ChangeListSchema');
    this.line('    {');
    this.line(`        public const string Sha256 = ${JSON.stringify(sha)};`);
    this.line(`        public const string Id = ${JSON.stringify(this.schema.$id)};`);
    for (const [name, d] of Object.entries(this.defs)) {
      if (d.type === 'string' && typeof d.pattern === 'string') this.line(`        public const string ${pascal(name)}Pattern = ${JSON.stringify(d.pattern)};`);
    }
    this.line('    }');
    this.line();

    const kinds: string[] = [];
    this.klass('ChangeList', this.schema.description as string, this.propsOf(this.schema, false));
    for (const [name, d] of Object.entries(this.defs)) {
      if (this.isScalarDef(name)) continue;
      if (Array.isArray(d.oneOf)) {
        this.klass(pascal(name), d.description as string, this.unionProps(d), 'One class holds every variant\'s fields; the schema says which may appear together.');
        continue;
      }
      const props = this.propsOf(d, false);
      this.klass(pascal(name), d.description as string, props);
      for (const [json, s] of Object.entries((d.properties ?? {}) as Record<string, Json>)) {
        if (Array.isArray(s.enum)) this.enumValues(pascal(name), json, s.enum as string[]);
        if (json === 'kind' && typeof s.const === 'string') kinds.push(s.const);
      }
    }
    this.line('    /// <summary>Every change kind the schema defines.</summary>');
    this.line('    public static class ChangeKinds');
    this.line('    {');
    for (const k of kinds) this.line(`        public const string ${pascal(k)} = ${JSON.stringify(k)};`);
    this.line(`        public static readonly string[] All = { ${kinds.map((k) => pascal(k)).join(', ')} };`);
    this.line('    }');
    this.line('}');
    return `${this.out.join('\n')}\n`;
  }
}

export function csharpFor(schema: Json, sha: string): string {
  return new CSharpWriter(schema).write(sha);
}

export async function generate(): Promise<Record<string, string>> {
  const bytes = readFileSync(SCHEMA_PATH);
  const sha = schemaSha256(bytes);
  const schema = JSON.parse(bytes.toString('utf8')) as Json;
  return {
    [TS_PATH]: await typescriptFor(schema, sha),
    [CS_PATH]: csharpFor(schema, sha),
    [SHA_PATH]: `${sha}\n`,
  };
}

if (import.meta.url === `file://${process.argv[1]}`) {
  const check = process.argv.includes('--check');
  const files = await generate();
  let stale = 0;
  for (const [path, text] of Object.entries(files)) {
    let current = '';
    try {
      current = readFileSync(path, 'utf8');
    } catch {
      // missing counts as stale
    }
    if (current === text) continue;
    if (check) {
      console.error(`stale: ${path.slice(ROOT.length)} (run npm run gen)`);
      stale++;
    } else {
      writeFileSync(path, text);
      console.log(`wrote ${path.slice(ROOT.length)}`);
    }
  }
  if (stale) process.exit(1);
  if (check) console.log('generated types are current');
}
