import { api, type Account } from '../api/client';
import { auth } from '../auth';
import { must } from '@node/html';
import type { Page } from '@node/router';
import { shell } from '@node/shell';
import { errorMessage, field, whileSaving } from '@node/ui';
import { anyFormTypedSinceDrawn, unsaved } from '@node/unsaved';
import { html, render } from '@node/view';

/**
 * A driver's own account: their name and how to reach them, their password,
 * and leaving - every contract taken back and every card taken away with the
 * account.
 *
 * The name and the password are kept by the HTTPExt API, as every node's
 * account page has them; leaving is the EMSP's, because what a driver holds
 * here goes with them.
 */
export const profilePage: Page = {

    title: 'Profile',

    render({ root, navigate }) {

        const content = shell(root, {
            active:    '/profile',
            title:     'Profile',
            subtitle:  'Who you are to this EMSP, your password, and leaving.'
        });

        render(content, html`<div class="loading">Loading ...</div>`);

        const username = auth.user?.username ?? '';

        let cancelled = false;
        let account: Account | null = null;


        function draw(): void {

            if (account === null)
                return;

            const name = textOf(account.name);

            render(content, html`
                <div class="cards">

                    <section class="card wide">

                        <h2><i class="fa-solid fa-user"></i> Your details</h2>

                        <form id="details-form" class="form-stack" @submit=${saveDetails}>

                            <div class="form-grid">
                                <label>Username
                                    <input type="text" name="username" value="${username}" disabled />
                                </label>
                                <label>Name
                                    <input type="text" name="name" maxlength="64" autocomplete="name" value="${name}" />
                                </label>
                                <label>E-mail address
                                    <input type="email" name="email" required maxlength="254" autocomplete="email" value="${account.email}" />
                                </label>
                                <label>Mobile phone
                                    <input type="tel" name="mobilePhone" maxlength="32" autocomplete="tel" value="${account.mobilePhone ?? ''}" />
                                </label>
                            </div>

                            <div class="form-actions">
                                <button type="submit" class="btn primary">Save</button>
                                <span id="details-note"  class="form-notice" role="status"></span>
                                <span id="details-error" class="form-error"  role="alert"></span>
                            </div>

                        </form>

                    </section>

                    <section class="card wide">

                        <h2><i class="fa-solid fa-key"></i> Your password</h2>

                        <form id="password-form" class="form-stack" @submit=${changePassword}>

                            <div class="form-grid">
                                <label>Current password
                                    <input type="password" name="current" required autocomplete="current-password" />
                                </label>
                                <label>New password
                                    <input type="password" name="password" required autocomplete="new-password" />
                                </label>
                                <label>New password, again
                                    <input type="password" name="password2" required autocomplete="new-password" />
                                </label>
                            </div>

                            <div class="form-actions">
                                <button type="submit" class="btn primary">Change the password</button>
                                <span id="password-note"  class="form-notice" role="status"></span>
                                <span id="password-error" class="form-error"  role="alert"></span>
                            </div>

                        </form>

                    </section>

                    <section class="card wide">

                        <h2><i class="fa-solid fa-user-xmark"></i> Leaving</h2>

                        <p class="hint">
                            Deleting your account takes back every contract certificate you hold and takes away every
                            RFID card you brought: neither charges anywhere afterwards. It cannot be undone. Type your
                            username to confirm.
                        </p>

                        <form id="delete-form" class="form-stack" @submit=${deleteAccount}>

                            <div class="form-grid">
                                <label>Your username
                                    <input type="text" name="confirm" required autocomplete="off" spellcheck="false" placeholder="${username}" />
                                </label>
                            </div>

                            <div class="form-actions">
                                <button type="submit" class="btn danger">Delete my account</button>
                                <span id="delete-error" class="form-error" role="alert"></span>
                            </div>

                        </form>

                    </section>

                </div>
            `);

        }


        function saveDetails(event: SubmitEvent): void {

            event.preventDefault();

            const form     = event.currentTarget as HTMLFormElement;
            const note     = must<HTMLElement>(content, '#details-note');
            const error    = must<HTMLElement>(content, '#details-error');

            note.textContent  = '';
            error.textContent = '';

            // What GET gave, with what the form changes: SET takes the whole
            // account, and what the page does not show goes back as it came.
            const changed: Account = { ...account!, name: { ...account!.name, en: field(form, 'name') }, email: field(form, 'email') };

            const mobile = field(form, 'mobilePhone');

            if (mobile.length > 0)
                changed.mobilePhone = mobile;
            else
                delete changed.mobilePhone;

            void (async () => {
                try
                {
                    account = await whileSaving(content, note, () => api.account.save(changed));

                    if (cancelled)
                        return;

                    draw();
                    form.reset();

                    must<HTMLElement>(content, '#details-note').textContent = 'Saved.';
                }
                catch (problem)
                {
                    if (!cancelled)
                        must<HTMLElement>(content, '#details-error').textContent = errorMessage(problem);
                }
            })();

        }


        function changePassword(event: SubmitEvent): void {

            event.preventDefault();

            const form     = event.currentTarget as HTMLFormElement;
            const note     = must<HTMLElement>(content, '#password-note');
            const error    = must<HTMLElement>(content, '#password-error');

            note.textContent  = '';
            error.textContent = '';

            // Untrimmed, like the sign-in: a space in a password is part of it.
            const current  = field(form, 'current',   false);
            const next     = field(form, 'password',  false);

            if (next !== field(form, 'password2', false)) {
                error.textContent = 'The two new passwords differ.';
                return;
            }

            void (async () => {
                try
                {
                    await whileSaving(content, note, () => api.me.changePassword(username, current, next));

                    if (cancelled)
                        return;

                    // Gone from the boxes either way: a password is not left
                    // standing in a page.
                    form.reset();
                    note.textContent = 'Your password was changed.';
                }
                catch (problem)
                {
                    if (!cancelled)
                        error.textContent = errorMessage(problem);
                }
            })();

        }


        function deleteAccount(event: SubmitEvent): void {

            event.preventDefault();

            const form   = event.currentTarget as HTMLFormElement;
            const error  = must<HTMLElement>(content, '#delete-error');
            const typed  = field(form, 'confirm');

            error.textContent = '';

            if (typed.toLowerCase() !== username.toLowerCase()) {
                error.textContent = `Type your username, ${username}, to confirm.`;
                return;
            }

            if (!window.confirm('Delete your account, and with it every contract and card you hold?\n\nThis cannot be undone.'))
                return;

            void (async () => {
                try
                {
                    await api.me.delete(typed);

                    if (cancelled)
                        return;

                    // The session went with the account.
                    auth.set(null);
                    navigate('/', true);
                }
                catch (problem)
                {
                    if (!cancelled)
                        error.textContent = errorMessage(problem);
                }
            })();

        }


        async function load(): Promise<void> {

            try
            {
                const loaded = await api.account.get(username);

                if (cancelled)
                    return;

                account = loaded;
                draw();
            }
            catch (problem)
            {
                if (!cancelled)
                    render(content, html`<div class="error-box">Your account could not be loaded: ${errorMessage(problem)}</div>`);
            }

        }


        // Details or a password typed and not yet sent are a draft like any
        // other page's: leaving asks first.
        const release = unsaved.heldBy(() => anyFormTypedSinceDrawn(content));

        void load();

        return () => { cancelled = true; release(); };

    }

};


/** Words of an account, in the first language they come in. */
function textOf(Text: Record<string, string> | undefined): string {
    return Text === undefined ? '' : (Text['en'] ?? Object.values(Text)[0] ?? '');
}
