/**
 * What the EMSP's own pages are held to: what every page of every kind of
 * node is, by the rules of WWCP_Node's test/pages.ts - each form held as a
 * draft, a Reload that asks first, every number read with numberField.
 *
 * Run with `npm test`. "@node/.." is WWCP_Node/Frontend, where the rules are.
 */

import { everyPageIn } from '@node/../test/pages.ts';


everyPageIn(new URL('./', import.meta.url), {

    withForms:  [ 'cards.ts', 'contracts.ts', 'partners.ts', 'profile.ts', 'tickets.ts', 'tokens.ts' ],

    notDrafts:  { 'signup.ts': 'signing up is the way in, as signing in is, and nothing typed there is the EMSP\'s to lose yet' }

});
