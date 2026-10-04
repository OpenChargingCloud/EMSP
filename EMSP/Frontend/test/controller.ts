/*
 * The EMSP as the stand-in node of WWCP_Node's test/node.ts, which every
 * kind's page tests share: a stand-in for fetch, and a document of happy-dom
 * to draw a page into. Only who it is is the EMSP's own.
 *
 * Imported first, before a page: lit-html looks for the document as it is
 * loaded, and the pages load it.
 *
 *   import { open, ... } from '../../test/controller.ts';
 *   const { tokensPage } = await import('./tokens.ts');
 */

import { standIn } from '@node/../test/node.ts';
export * from '@node/../test/node.ts';

standIn({ name: 'EMSP', icon: 'fa-handshake' });
