import { auth as nodeAuth, type AuthState } from '@node/auth';
import type { Me, Resource } from './api/client';


// Who is signed in: the node's one AuthState, which the pages and the log
// share and which forgets the user at the first 401 by itself - typed with
// what an account of this EMSP may do, its OCPI and its contracts among it.
export const auth = nodeAuth as unknown as AuthState<Me, Resource>;
