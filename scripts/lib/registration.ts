// The registration logic lives with the server since flo2-rvt 0.5.0 (server/src/aps/registration.ts),
// which runs it on a person's app from flo2.io once they approve the setup there. The owner's
// command line (scripts/flo2-rvt.ts) runs the same code from here.
export * from '../../server/src/aps/registration.ts';
