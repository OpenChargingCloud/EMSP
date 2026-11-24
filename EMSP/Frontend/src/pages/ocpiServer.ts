import { ocpiServerAPI as api, type OCPIServer } from '../api/client';
import { toURL } from '@node/basePath';
import type { Page } from '@node/router';
import { reloadButton, shell } from '@node/shell';
import { errorMessage } from '@node/ui';
import { html, render, repeat } from '@node/view';

/** How a client may authenticate, in a few words. */
const IN_WORDS: Record<string, string> = {
    httpAuthWithoutTLS:    'a password, without TLS',
    httpAuthOverTLS:       'a password, over TLS',
    tlsClientCertificate:  'a TLS client certificate'
};

/**
 * The WebSocket server of OCPI the CPOs connect to, over a WebSocket or SSH,
 * as it is: whether it listens, how one may sign in, and who is connected now.
 *
 * Read-only: the server is changed in the "webSocketServer" section of the
 * file, and the EMSP started again. Who may connect is changed on the pages
 * below it - the logins, the accepted chains and the connections, which are
 * every WebSocket server's pages.
 */
export const ocpiServerPage: Page = {

    title: 'CPOs that connect',

    render({ root }) {

        const content = shell(root, {
            active:    '/configuration/ocpi-server',
            title:     'CPOs that connect',
            subtitle:  'the WebSocket server of OCPI they connect to, and who is connected.',
            actions:   reloadButton(() => load())
        });

        render(content, html`<div class="loading">Loading ...</div>`);

        let cancelled = false;


        function draw(server: OCPIServer): void {

            const configuration = server.configuration;

            render(content, html`

                <div class="cards">

                    <section class="card">

                        <h2><i class="fa-solid fa-server"></i> The server</h2>

                        <div class="kv">
                            <span class="k">WebSocket</span>
                            <span class="v" id="ocpi-websocket">
                                ${server.state.running
                                      ? html`<span class="badge ok">listening</span> on port ${configuration.port ?? '-'}${server.state.tls ? ', over TLS' : ', without TLS'}`
                                      : configuration.enabled === true
                                            ? html`<span class="badge warn">switched on, not listening</span>`
                                            : html`<span class="badge">switched off</span>`}
                            </span>
                            <span class="k">SSH</span>
                            <span class="v" id="ocpi-ssh">
                                ${server.state.ssh
                                      ? html`<span class="badge ok">served</span> on port ${server.state.sshPort ?? '-'}, as the subsystem <code>ocpi@open.charging.cloud</code>`
                                      : html`<span class="badge">not served</span>`}
                            </span>
                            <span class="k">Signing in</span>
                            <span class="v">${(configuration.clientAuthentication ?? []).map(way => IN_WORDS[way] ?? way).join(', ') || '-'}</span>
                            <span class="k">Revocation</span>
                            <span class="v">${configuration.checkCertificateRevocation ? 'a client certificate is checked against its CRL' : 'not checked'}</span>
                        </div>

                        <p class="hint">
                            Changed in the <code>webSocketServer</code> section of the file, and the EMSP started again.
                            Who may connect: the <a href="${toURL('/configuration/ocpi-server/logins')}">logins</a>,
                            the <a href="${toURL('/configuration/ocpi-server/trust')}">accepted chains</a> and the
                            <a href="${toURL('/configuration/connections')}">connections</a> - a CPO is written down as
                            both, a connection of the type "CPO" that lets it in and a login it signs in with.
                        </p>

                    </section>

                    <section class="card">

                        <h2><i class="fa-solid fa-plug"></i> Connected now</h2>

                        ${server.connected.length === 0
                              ? html`<p class="muted" id="ocpi-connected">No CPO is connected.</p>`
                              : html`<ul id="ocpi-connected">${repeat(server.connected, login => login, login => html`<li><code>${login}</code></li>`)}</ul>`}

                    </section>

                </div>
            `);

        }


        async function load(): Promise<void> {

            try
            {

                const server = await api.get();

                if (!cancelled)
                    draw(server);

            }
            catch (problem)
            {
                if (!cancelled)
                    render(content, html`<div class="error-box">${errorMessage(problem)}</div>`);
            }

        }

        void load();

        return () => { cancelled = true; };

    }

};
