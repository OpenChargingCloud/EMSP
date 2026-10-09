import { auth } from '../auth';
import { toURL } from '@node/basePath';
import { config } from '@node/config';
import type { Page } from '@node/router';
import { firstPageOfTheMenu } from '@node/start';
import { html, render } from '@node/view';

/**
 * Where "/" leads. Somebody not signed in is welcomed: a driver is shown how
 * to sign up or in, and whoever runs this EMSP how root signs in. Somebody
 * signed in goes where they are going anyway - an operator to the
 * configuration, a driver to what they charged, anybody else to the first
 * page of the menu they may open.
 *
 * Not every node's "/" alone: the drivers' pages come first in the menu, and
 * an operator, who may see everybody's, would land on them rather than on
 * what an operator is here for. And a public page, because the first thing a
 * driver sees of this EMSP should not be a password box that is not theirs.
 */
export const homePage: Page = {

    title: 'Welcome',

    render(context) {

        const { root, navigate } = context;

        if (auth.user !== null) {

            if (auth.can('configuration', 'read'))
                navigate('/configuration', true);

            else if (auth.can('tokens', 'run') || auth.can('contracts', 'run'))
                navigate('/charging', true);

            else
                firstPageOfTheMenu.render(context);

            return;

        }

        render(root, html`
            <section class="login welcome" id="welcome">

                <h1><i class="fa-solid fa-handshake"></i> Welcome</h1>

                <p class="muted">
                    This e-mobility service provider lets its drivers charge at the charging stations of its
                    roaming partners - with an RFID card, or with a contract certificate in the vehicle.
                </p>

                <div class="welcome-choices">

                    <div class="welcome-choice" id="for-drivers">
                        <h2><i class="fa-solid fa-car"></i> Drivers</h2>
                        <p>
                            Sign up for an account to bring your RFID cards, ask for a contract certificate, and see
                            where you charged and what it cost.
                        </p>
                        <div class="form-actions">
                            <a class="btn primary" id="to-signup" href="${toURL('/signup')}">Sign up</a>
                            <a class="btn" id="to-signin" href="${toURL('/login')}">Sign in</a>
                        </div>
                    </div>

                    <div class="welcome-choice" id="for-operators">
                        <h2><i class="fa-solid fa-screwdriver-wrench"></i> Operators</h2>
                        <p>
                            Whoever runs this EMSP signs in as <code>root</code>. Its password was printed once, in
                            a box on the console the EMSP was first started from; sign in with it, and give
                            others accounts of their own.
                        </p>
                        <div class="form-actions">
                            <a class="btn" id="to-root" href="${toURL('/login')}">Sign in as root</a>
                        </div>
                    </div>

                </div>

                <p class="small muted">EMSP ${config.serverVersion} &middot; web ${config.frontendVersion}</p>

            </section>
        `);

    }

};
