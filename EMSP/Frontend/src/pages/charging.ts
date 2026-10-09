import { api, type Charged, type Charging } from '../api/client';
import { toTheMinute } from '../time';
import { toURL } from '@node/basePath';
import type { Page } from '@node/router';
import { reloadButton, shell } from '@node/shell';
import { errorMessage, formatNumber, formatTimestamp } from '@node/ui';
import { html, nothing, render, type TemplateResult } from '@node/view';

/**
 * What a driver charged: the sessions and the charge detail records the
 * roaming partners pushed with one of the driver's cards or contracts in
 * them, the newest first. A session is what is going on, or went on, at a
 * charging station; a charge detail record is what the partner bills.
 */
export const chargingPage: Page = {

    title: 'Charging',

    render({ root }) {

        const content = shell(root, {
            active:    '/charging',
            title:     'Charging',
            subtitle:  'Where you charged with your cards and contracts, and what it cost.',
            actions:   reloadButton(() => load())
        });

        render(content, html`<div class="loading">Loading ...</div>`);

        let cancelled = false;


        function draw(charging: Charging): void {

            const now = charging.sessions.filter(session => isGoingOn(session));

            render(content, html`

                ${charging.cards === 0 && charging.contracts === 0 ? html`
                    <div class="notice" id="nothing-to-charge-with">
                        You have nothing to charge with yet: bring an <a href="${toURL('/cards')}">RFID card</a>, or
                        ask for a <a href="${toURL('/contracts')}">contract certificate</a> for your vehicle.
                    </div>
                ` : nothing}

                <div class="cards">

                    ${now.length > 0 ? html`
                        <section class="card wide" id="charging-now">
                            <h2><i class="fa-solid fa-bolt"></i> Charging now</h2>
                            ${table(now, false)}
                        </section>
                    ` : nothing}

                    <section class="card wide" id="charge-records">
                        <h2><i class="fa-solid fa-file-invoice"></i> Charge detail records</h2>
                        <p class="hint">What the charge point operators billed, once a charge was over.</p>
                        ${charging.cdrs.length === 0
                              ? html`<p class="muted">No charge detail record yet.</p>`
                              : table(charging.cdrs, true)}
                    </section>

                    <section class="card wide" id="sessions">
                        <h2><i class="fa-solid fa-plug-circle-bolt"></i> Sessions</h2>
                        <p class="hint">Every charge as the charge point operators reported it while it went on.</p>
                        ${charging.sessions.length === 0
                              ? html`<p class="muted">No session yet.</p>`
                              : table(charging.sessions, false)}
                    </section>

                </div>
            `);

        }


        function table(items: Charged[], billed: boolean): TemplateResult {

            return html`
                <div class="table-scroll">
                    <table class="table">
                        <thead>
                            <tr>
                                <th>Started</th>
                                <th>${billed ? 'Ended' : 'Status'}</th>
                                <th>Where</th>
                                <th class="right">Energy</th>
                                <th class="right">Cost</th>
                                <th>With</th>
                            </tr>
                        </thead>
                        <tbody>
                            ${items.map(item => html`
                                <tr data-id="${item.id}">
                                    <td>${item.start ? formatTimestamp(item.start, toTheMinute) : '-'}</td>
                                    <td>${billed
                                              ? (item.end ? formatTimestamp(item.end, toTheMinute) : '-')
                                              : html`<span class="badge ${isGoingOn(item) ? 'ok' : ''}">${item.status ?? '-'}</span>`}</td>
                                    <td>
                                        ${item.location ?? '-'}
                                        ${item.address ? html`<div class="small muted">${item.address}</div>` : nothing}
                                        ${item.evse    ? html`<div class="small muted">${item.evse}</div>`    : nothing}
                                    </td>
                                    <td class="right">${item.kWh === null ? '-' : `${formatNumber(item.kWh)} kWh`}</td>
                                    <td class="right">${item.cost === null ? '-' : `${formatNumber(item.cost)} ${item.currency ?? ''}`}</td>
                                    <td class="small"><code>${item.token ?? '-'}</code></td>
                                </tr>
                            `)}
                        </tbody>
                    </table>
                </div>
            `;

        }


        async function load(): Promise<void> {

            try
            {
                const charging = await api.charging();

                if (!cancelled)
                    draw(charging);
            }
            catch (problem)
            {
                if (!cancelled)
                    render(content, html`<div class="error-box">What you charged could not be loaded: ${errorMessage(problem)}</div>`);
            }

        }

        void load();

        return () => { cancelled = true; };

    }

};


/** Whether a session still goes on: charging, about to, or held for its driver. */
function isGoingOn(Session: Charged): boolean {
    return Session.status === 'ACTIVE' || Session.status === 'PENDING' || Session.status === 'RESERVATION';
}
