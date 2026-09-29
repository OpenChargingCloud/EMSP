import './styles/app.scss';

// FontAwesome: the CSS ends up in the extracted stylesheet, the referenced
// font files become hashed assets below /assets/.
import '@fortawesome/fontawesome-free/css/fontawesome.css';
import '@fortawesome/fontawesome-free/css/solid.css';

import { toURL } from '@node/basePath';
import { html } from '@node/html';
import { nodeMenu, startNode } from '@node/start';

import { certificatesPage }   from './pages/certificates';
import { configurationPage }  from './pages/configuration';
import { contractsPage }      from './pages/contracts';
import { dnsPage }            from './pages/dns';
import { homePage }           from './pages/home';
import { ntsPage }            from './pages/nts';
import { ocpiPage }           from './pages/ocpi';
import { partnersPage }       from './pages/partners';
import { tokensPage }         from './pages/tokens';
import { roamingDataPages }   from './pages/roamingData';
import { signUpPage }         from './pages/signup';

// What an EMSP has pages for beside what every node has: its drivers'
// contracts, its OCPI side - who it is, its partners, its tokens - and what
// the partners pushed. The sign-in, the log, the frame and following the log
// while somebody it is for is signed in are every node's - see WWCP_Node's
// start.ts.
startNode({

    name:  'EMSP',
    icon:  'fa-handshake',

    menu: [
        { path: '/contracts',                       label: 'Contracts',         icon: 'fa-file-contract',  permission: [ 'contracts:run', 'contracts:edit' ] },
        nodeMenu.configuration([
            nodeMenu.dns,
            nodeMenu.nts,
            nodeMenu.certificates,
            { path: '/configuration/ocpi',            label: 'OCPI',              icon: 'fa-plug',           permission: [ 'ocpi:read' ]     },
            { path: '/configuration/ocpi/partners',   label: 'Roaming partners',  icon: 'fa-handshake',      permission: [ 'partners:read' ] },
            { path: '/configuration/ocpi/tokens',     label: 'Tokens',            icon: 'fa-id-card',        permission: [ 'tokens:read' ]   }
        ]),
        {
            path:        '/roaming',
            label:       'Roaming data',
            icon:        'fa-database',
            permission:  [ 'ocpi:read' ],
            // Each with the permission of the whole, as its route asks it: a
            // page below an entry without one is everybody's, and would stand
            // in for the entry for whoever may not open it - a driver.
            children: [
                { path: '/roaming/locations',  label: 'Locations',              icon: 'fa-map-location-dot',  permission: [ 'ocpi:read' ] },
                { path: '/roaming/tariffs',    label: 'Tariffs',                icon: 'fa-tags',              permission: [ 'ocpi:read' ] },
                { path: '/roaming/sessions',   label: 'Charging sessions',      icon: 'fa-bolt',              permission: [ 'ocpi:read' ] },
                { path: '/roaming/cdrs',       label: 'Charge detail records',  icon: 'fa-file-invoice',      permission: [ 'ocpi:read' ] }
            ]
        },
        nodeMenu.logs
    ],

    pages: {

        // "/" is the EMSP's own: an operator to the configuration, although
        // the contracts come first in the menu - see pages/home.ts.
        '/':                              homePage,
        '/configuration':                 configurationPage,
        '/configuration/dns':             dnsPage,
        '/configuration/nts':             ntsPage,
        '/configuration/certificates':    certificatesPage,
        '/configuration/ocpi':            ocpiPage,
        '/configuration/ocpi/partners':   partnersPage,
        '/configuration/ocpi/tokens':     tokensPage,

        '/roaming':                       roamingDataPages.locations,
        '/roaming/locations':             roamingDataPages.locations,
        '/roaming/tariffs':               roamingDataPages.tariffs,
        '/roaming/sessions':              roamingDataPages.sessions,
        '/roaming/cdrs':                  roamingDataPages.cdrs,

        '/contracts':                     contractsPage

    },

    // Anybody may sign up for a contract; whoever is signed out on its page
    // stays there rather than being sent to the sign-in.
    publicPages: {
        '/signup':  signUpPage
    },

    signIn: {
        line:   'Sign in to look after this EMSP: its roaming partners, its tokens and its log.',
        below:  () => html`
                    <p class="small login-hint">
                        A driver? <a href="${toURL('/signup')}">Sign up</a> for an account and a contract certificate.
                    </p>
                `
    }

});
