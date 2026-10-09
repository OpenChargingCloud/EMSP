import './styles/app.scss';

// FontAwesome: the CSS ends up in the extracted stylesheet, the referenced
// font files become hashed assets below /assets/.
import '@fortawesome/fontawesome-free/css/fontawesome.css';
import '@fortawesome/fontawesome-free/css/solid.css';

import { toURL } from '@node/basePath';
import { html } from '@node/view';
import { nodeMenu, startNode } from '@node/start';

import { auth }               from './auth';
import { cardsPage }          from './pages/cards';
import { chargingPage }       from './pages/charging';
import { configurationPage }  from './pages/configuration';
import { contractsPage }      from './pages/contracts';
import { homePage }           from './pages/home';
import { ocpiPage }           from './pages/ocpi';
import { partnersPage }       from './pages/partners';
import { profilePage }        from './pages/profile';
import { tokensPage }         from './pages/tokens';
import { roamingDataPages }   from './pages/roamingData';
import { signUpPage }         from './pages/signup';

// What an EMSP has pages for beside what every node has: its drivers'
// contracts, its OCPI side - who it is, its partners, its tokens - and what
// the partners pushed. The sign-in, the log, the frame, following the log
// while somebody it is for is signed in, the name servers, the time servers
// and the certificate store are every node's - see WWCP_Node's start.ts.
startNode({

    name:  'EMSP',
    icon:  'fa-handshake',

    // A driver's pages first - what they charged, their cards, their
    // contracts, their profile - and then whoever runs the EMSP's. The
    // profile is a driver's alone: whoever runs the EMSP has the account
    // page every node has, behind the name at the foot of the menu.
    menu: [
        { path: '/charging',                        label: 'Charging',          icon: 'fa-bolt',           permission: () => auth.can('tokens', 'run') || auth.can('contracts', 'run') },
        { path: '/cards',                           label: 'RFID cards',        icon: 'fa-id-card',        permission: [ 'tokens:run', 'tokens:edit' ] },
        { path: '/contracts',                       label: 'Contracts',         icon: 'fa-file-contract',  permission: [ 'contracts:run', 'contracts:edit' ] },
        { path: '/profile',                         label: 'Profile',           icon: 'fa-user',           permission: () => auth.can('tokens', 'run') && !auth.can('configuration', 'read') },
        nodeMenu.configuration([
            nodeMenu.dns,
            nodeMenu.nts,
            nodeMenu.ssh,
            nodeMenu.certificates,
            nodeMenu.identities,
            { path: '/configuration/ocpi',           label: 'OCPI',              icon: 'fa-plug',           permission: [ 'ocpi:read' ]     },
            { path: '/configuration/ocpi/partners',   label: 'Roaming partners',  icon: 'fa-handshake',      permission: [ 'partners:read' ] },
            { path: '/configuration/ocpi/tokens',     label: 'Tokens',            icon: 'fa-id-card',        permission: [ 'tokens:read' ]   }
        ]),
        {
            path:        '/roaming',
            label:       'Roaming data',
            icon:        'fa-database',
            permission:  [ 'ocpi:read' ],
            // The pages below are the entry's, and shown to whoever may read
            // the OCPI side - see visibleMenu() in WWCP_Node's shell.ts.
            children: [
                { path: '/roaming/locations',  label: 'Locations',              icon: 'fa-map-location-dot' },
                { path: '/roaming/tariffs',    label: 'Tariffs',                icon: 'fa-tags'             },
                { path: '/roaming/sessions',   label: 'Charging sessions',      icon: 'fa-bolt'             },
                { path: '/roaming/cdrs',       label: 'Charge detail records',  icon: 'fa-file-invoice'     }
            ]
        },
        nodeMenu.logs
    ],

    // The certificate store in every node's words, but for what of it nothing
    // on this EMSP uses yet: the roots of Plug & Charge and the client roots,
    // which no chain is checked against, and the identity - who this EMSP is
    // as a client, on the Identities page - which nothing presents. The MO
    // root the contracts are signed below is not in the store; it is shown on
    // the Contracts page.
    certificates: {
        hints: {
            believes:  html`
                Trust anchors. Every switched-on root of a kind is believed at once. A TLS root is what a
                time server or a name server may be vouched for by, beside the roots this machine already
                believes; the roots of Plug &amp; Charge and the client roots are kept here, and nothing on
                this EMSP checks a chain against them yet.
            `,
            presents:  html`
                Who this EMSP is as a client, with its private key, to be known by in TLS when it
                connects to a partner. Kept here, and presented by nothing on this EMSP yet.
            `
        }
    },

    pages: {

        '/configuration':                 configurationPage,
        '/configuration/ocpi':            ocpiPage,
        '/configuration/ocpi/partners':   partnersPage,
        '/configuration/ocpi/tokens':     tokensPage,

        '/roaming':                       roamingDataPages.locations,
        '/roaming/locations':             roamingDataPages.locations,
        '/roaming/tariffs':               roamingDataPages.tariffs,
        '/roaming/sessions':              roamingDataPages.sessions,
        '/roaming/cdrs':                  roamingDataPages.cdrs,

        '/charging':                      chargingPage,
        '/cards':                         cardsPage,
        '/contracts':                     contractsPage,
        '/profile':                       profilePage

    },

    // "/" welcomes whoever is not signed in, and sends whoever is where they
    // are going - see pages/home.ts. Anybody may sign up; whoever is signed
    // out on either page stays there rather than being sent to the sign-in.
    publicPages: {
        '/':        homePage,
        '/signup':  signUpPage
    },

    signIn: {
        line:   'Sign in - as a driver, or as root to look after this EMSP.',
        below:  () => html`
                    <p class="small login-hint">
                        A driver without an account? <a href="${toURL('/signup')}">Sign up</a> to bring your RFID
                        cards and ask for a contract certificate.
                    </p>
                `
    }

});
