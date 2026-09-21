import { api } from '../api/client';
import { auth } from '../auth';
import { toURL } from '../basePath';
import { config } from '../config';
import { html, must, render } from '../html';
import type { Page } from '../router';
import { errorMessage, field } from '../ui';

/**
 * A driver signing up: a username, an e-mail address and a password, and the
 * EMSP makes an account that may ask for contract certificates and nothing
 * else. The account is signed in the moment it exists, so the next page is
 * the contracts.
 */
export const signUpPage: Page = {

    title: 'Sign up',

    render({ root, navigate }) {

        if (auth.user) {
            navigate('/contracts', true);
            return;
        }

        render(root, html`
            <section class="login">

                <h1><i class="fa-solid fa-handshake"></i> EMSP</h1>
                <p class="muted">
                    Sign up as a driver. With an account you can ask this EMSP for a contract
                    certificate - what your vehicle presents at a charging station instead of a card.
                </p>

                <form id="signup-form" class="form-stack">
                    <label>Username
                        <input name="username" required autocomplete="username" autofocus minlength="4" maxlength="32"
                               pattern="[A-Za-z0-9]([A-Za-z0-9._\\-]*[A-Za-z0-9])?"
                               title="4 to 32 letters, digits, dots, dashes or underscores, starting and ending with a letter or digit" />
                    </label>
                    <label>E-mail address
                        <input name="email" type="email" required autocomplete="email" maxlength="254" />
                    </label>
                    <label>Display name
                        <input name="displayName" autocomplete="name" maxlength="64" placeholder="optional" />
                    </label>
                    <label>Password
                        <input name="password" type="password" required autocomplete="new-password" />
                    </label>
                    <label>Password, again
                        <input name="password2" type="password" required autocomplete="new-password" />
                    </label>
                    <div class="form-actions">
                        <button type="submit" class="btn primary">Sign up</button>
                        <span id="form-error" class="form-error" role="alert"></span>
                    </div>
                </form>

                <p class="small login-hint">
                    Already have an account? <a href="${toURL('/login')}">Sign in</a>.
                </p>

                <p class="small muted">EMSP ${config.serverVersion} &middot; web ${config.frontendVersion}</p>

            </section>
        `);

        const form    = must<HTMLFormElement>(root, '#signup-form');
        const error   = must<HTMLElement>(root, '#form-error');
        const button  = must<HTMLButtonElement>(form, 'button[type="submit"]');

        form.addEventListener('submit', event => {

            event.preventDefault();
            error.textContent = '';

            // Untrimmed, like the sign-in: a space in a password is part of it.
            const password = field(form, 'password', false);

            if (password !== field(form, 'password2', false)) {
                error.textContent = 'The two passwords differ.';
                return;
            }

            button.disabled = true;

            void (async () => {
                try
                {
                    auth.set(await api.auth.signUp(field(form, 'username'), field(form, 'email'), password, field(form, 'displayName')));
                    navigate('/contracts', true);
                }
                catch (problem)
                {
                    error.textContent = errorMessage(problem);
                    button.disabled   = false;
                }
            })();

        });

    }

};
