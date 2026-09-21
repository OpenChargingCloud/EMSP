import './styles/app.scss';

// FontAwesome: the CSS ends up in the extracted stylesheet, the referenced
// font files become hashed assets below /assets/.
import '@fortawesome/fontawesome-free/css/fontawesome.css';
import '@fortawesome/fontawesome-free/css/solid.css';

import { auth } from './auth';
import { html, must, render } from './html';
import { logs } from './logs/store';
import { Router } from './router';

import { configurationPage }  from './pages/configuration';
import { contractsPage }      from './pages/contracts';
import { dnsPage }            from './pages/dns';
import { homePage }           from './pages/home';
import { ntsPage }            from './pages/nts';
import { ocpiPage }           from './pages/ocpi';
import { partnersPage }       from './pages/partners';
import { tokensPage }         from './pages/tokens';
import { roamingDataPages }   from './pages/roamingData';
import { loginPage }          from './pages/login';
import { signUpPage }         from './pages/signup';
import { logsPage }           from './pages/logs';
import { notFoundPage }       from './pages/notFound';
import { fromURL } from './basePath';


const root = document.getElementById('app');

if (root === null)
    throw new Error("The '#app' element is missing!");

render(root, html`<div id="page" class="page"></div>`);

const router = new Router({
    routes: [
        // "/" is a route of its own rather than nothing: the sign-in remembers
        // where somebody was going, and for the first visit that is "/". Where
        // it leads depends on who arrived - an operator to the configuration,
        // a driver to their contracts.
        { path: '/',                              page: homePage,                   guard: auth.requireSignIn },
        { path: '/configuration',                 page: configurationPage,          guard: auth.requireSignIn },
        { path: '/configuration/dns',             page: dnsPage,                    guard: auth.requireSignIn },
        { path: '/configuration/nts',             page: ntsPage,                    guard: auth.requireSignIn },
        { path: '/configuration/ocpi',            page: ocpiPage,                   guard: auth.requireSignIn },
        { path: '/configuration/ocpi/partners',   page: partnersPage,               guard: auth.requireSignIn },
        { path: '/configuration/ocpi/tokens',     page: tokensPage,                 guard: auth.requireSignIn },

        { path: '/roaming',                       page: roamingDataPages.locations, guard: auth.requireSignIn },
        { path: '/roaming/locations',             page: roamingDataPages.locations, guard: auth.requireSignIn },
        { path: '/roaming/tariffs',               page: roamingDataPages.tariffs,   guard: auth.requireSignIn },
        { path: '/roaming/sessions',              page: roamingDataPages.sessions,  guard: auth.requireSignIn },
        { path: '/roaming/cdrs',                  page: roamingDataPages.cdrs,      guard: auth.requireSignIn },

        { path: '/contracts',                     page: contractsPage,              guard: auth.requireSignIn },

        { path: '/logs',                          page: logsPage,                   guard: auth.requireSignIn },
        { path: '/login',                         page: loginPage },
        { path: '/signup',                        page: signUpPage }
    ],
    outlet:       must<HTMLElement>(root, '#page'),
    notFound:     notFoundPage,
    titleSuffix:  ' · EMSP'
});

// Signed in: follow the EMSP's log from now on, whichever page is open - so
// that opening the Logs page shows what happened while somebody was reading
// the configuration, and not an empty list. Only for whoever may read the
// log: a driver may not, and the stream would answer 403.
// Signed out - by the button, or because the session expired and a request
// came back with 401: close the stream, forget the log, show the sign-in.
auth.onChange(user => {

    if (user !== null) {

        if (auth.can('readConfiguration'))
            logs.start();

        return;

    }

    logs.stop();

    const route = fromURL(location.pathname);

    if (route !== '/login' && route !== '/signup')
        router.navigate(auth.requireSignIn(new URL(location.href)) ?? '/login', true);

});

// Find out who is signed in before the first page renders, so that a reload on
// a deep URL does not flash the sign-in page on its way back to where it was.
void (async () => {
    await auth.refresh();
    router.start();
})();
