import { api, type RoamingDataKind, type RoamingItem } from '../api/client';
import { html, must, render, type HTMLFragment } from '../html';
import type { Page } from '../router';
import { shell } from '../shell';
import { errorMessage, formatTimestamp, formatValue } from '../ui';

/**
 * What the roaming partners pushed into this EMSP: the locations they
 * operate, their tariffs, the charging sessions of this EMSP's customers and
 * the charge detail records that settle them.
 *
 * Read-only, because it is theirs: a CPO pushes a location and a CPO changes
 * it, and an EMSP that edited a copy would be an EMSP with a location nobody
 * else has. The columns below are the handful of fields worth a glance; the
 * whole object, as OCPI wrote it, is behind every row.
 */

interface Column {
    label:   string;
    /** The value of the cell, out of the object as OCPI writes it. */
    value:   (item: RoamingItem) => unknown;
    /** Whether the value is a moment to be written as one. */
    time?:   boolean;
}

interface KindPage {
    kind:      RoamingDataKind;
    path:      string;
    title:     string;
    subtitle:  string;
    icon:      string;
    columns:   Column[];
}

const string = (item: RoamingItem, key: string): unknown => item[key];

const kinds: KindPage[] = [
    {
        kind:      'locations',
        path:      '/roaming/locations',
        title:     'Locations',
        subtitle:  'Where the partners let this EMSP\'s customers charge.',
        icon:      'fa-map-location-dot',
        columns:   [
            { label: 'ID',        value: item => string(item, 'id') },
            { label: 'Operator',  value: item => `${string(item, 'country_code') ?? ''}-${string(item, 'party_id') ?? ''}` },
            { label: 'Name',      value: item => string(item, 'name') },
            { label: 'Address',   value: item => [string(item, 'address'), string(item, 'postal_code'), string(item, 'city'), string(item, 'country')].filter(part => part).join(', ') },
            { label: 'EVSEs',     value: item => Array.isArray(item['evses']) ? (item['evses'] as unknown[]).length : 0 },
            { label: 'Updated',   value: item => string(item, 'last_updated'), time: true }
        ]
    },
    {
        kind:      'tariffs',
        path:      '/roaming/tariffs',
        title:     'Tariffs',
        subtitle:  'What the partners charge, as they told this EMSP.',
        icon:      'fa-tags',
        columns:   [
            { label: 'ID',        value: item => string(item, 'id') },
            { label: 'Operator',  value: item => `${string(item, 'country_code') ?? ''}-${string(item, 'party_id') ?? ''}` },
            { label: 'Currency',  value: item => string(item, 'currency') },
            { label: 'Type',      value: item => string(item, 'type') },
            { label: 'Elements',  value: item => Array.isArray(item['elements']) ? (item['elements'] as unknown[]).length : 0 },
            { label: 'Updated',   value: item => string(item, 'last_updated'), time: true }
        ]
    },
    {
        kind:      'sessions',
        path:      '/roaming/sessions',
        title:     'Charging sessions',
        subtitle:  'The sessions of this EMSP\'s customers, as the partners report them.',
        icon:      'fa-bolt',
        columns:   [
            { label: 'ID',        value: item => string(item, 'id') },
            { label: 'Operator',  value: item => `${string(item, 'country_code') ?? ''}-${string(item, 'party_id') ?? ''}` },
            { label: 'Started',   value: item => string(item, 'start_date_time') ?? string(item, 'start_datetime'), time: true },
            { label: 'Ended',     value: item => string(item, 'end_date_time')   ?? string(item, 'end_datetime'),   time: true },
            { label: 'kWh',       value: item => string(item, 'kwh') },
            { label: 'Status',    value: item => string(item, 'status') },
            { label: 'Cost',      value: item => cost(item['total_cost']) },
            { label: 'Updated',   value: item => string(item, 'last_updated'), time: true }
        ]
    },
    {
        kind:      'cdrs',
        path:      '/roaming/cdrs',
        title:     'Charge detail records',
        subtitle:  'What the partners will invoice this EMSP for.',
        icon:      'fa-file-invoice',
        columns:   [
            { label: 'ID',        value: item => string(item, 'id') },
            { label: 'Operator',  value: item => `${string(item, 'country_code') ?? ''}-${string(item, 'party_id') ?? ''}` },
            { label: 'Started',   value: item => string(item, 'start_date_time') ?? string(item, 'start_datetime'), time: true },
            { label: 'Ended',     value: item => string(item, 'end_date_time')   ?? string(item, 'stop_datetime'),  time: true },
            { label: 'Energy',    value: item => string(item, 'total_energy') },
            { label: 'Cost',      value: item => cost(item['total_cost']) },
            { label: 'Updated',   value: item => string(item, 'last_updated'), time: true }
        ]
    }
];

/** A price as OCPI writes it: a number in 2.1.1, an object with excl_vat/incl_vat later. */
function cost(value: unknown): unknown {

    if (value === null || value === undefined)
        return null;

    if (typeof value === 'object' && 'excl_vat' in value)
        return `${formatValue((value as { excl_vat: unknown }).excl_vat)}${'incl_vat' in value ? ` (${formatValue((value as { incl_vat: unknown }).incl_vat)} incl. VAT)` : ''}`;

    return value;

}


function page(definition: KindPage): Page {

    return {

        title: definition.title,

        render({ root }) {

            const content = shell(root, {
                active:    definition.path,
                title:     definition.title,
                subtitle:  definition.subtitle,
                actions:   html`<button type="button" id="reload" class="btn small">Reload</button>`
            });

            render(content, html`<div class="loading">Loading ...</div>`);

            must<HTMLButtonElement>(root, '#reload').addEventListener('click', () => void load());

            let cancelled = false;

            function draw(items: RoamingItem[]): void {

                render(content, html`
                    <section class="card wide">

                        <h2><i class="fa-solid ${definition.icon}"></i> ${definition.title} <span class="chip">${items.length}</span></h2>

                        ${items.length === 0
                              ? html`<p class="muted">Nothing has been pushed yet. A partner sends these once the peering is complete.</p>`
                              : html`
                                  <div class="table-scroll">
                                      <table class="table">
                                          <thead>
                                              <tr>
                                                  ${definition.columns.map(column => html`<th>${column.label}</th>`)}
                                                  <th>OCPI</th>
                                                  <th></th>
                                              </tr>
                                          </thead>
                                          <tbody>
                                              ${items.map((item, index) => row(item, index))}
                                          </tbody>
                                      </table>
                                  </div>
                              `}

                    </section>
                `);

                content.querySelectorAll<HTMLButtonElement>('.show-raw').forEach(button => {
                    button.addEventListener('click', () => {

                        const raw = content.querySelector<HTMLElement>(`#raw-${button.dataset.index}`);

                        if (raw) {
                            raw.hidden          = !raw.hidden;
                            button.textContent  = raw.hidden ? 'JSON' : 'Hide';
                        }

                    });
                });

            }

            function row(item: RoamingItem, index: number): HTMLFragment {

                const { version, ...rest } = item;

                return html`
                    <tr>
                        ${definition.columns.map(column => {
                            const value = column.value(item);
                            return html`<td class="small">${column.time && typeof value === 'string' ? formatTimestamp(value) : formatValue(value)}</td>`;
                        })}
                        <td class="small">${version}</td>
                        <td class="right">
                            <button type="button" class="btn small show-raw" data-index="${index}">JSON</button>
                        </td>
                    </tr>
                    <tr id="raw-${index}" hidden>
                        <td colspan="${definition.columns.length + 2}">
                            <pre class="pem">${JSON.stringify(rest, null, 2)}</pre>
                        </td>
                    </tr>
                `;

            }

            async function load(): Promise<void> {

                try
                {
                    const data = await api.ocpi.data(definition.kind);

                    if (!cancelled)
                        draw(data.items);
                }
                catch (problem)
                {
                    if (!cancelled)
                        render(content, html`<div class="error-box">The ${definition.title.toLowerCase()} could not be loaded: ${errorMessage(problem)}</div>`);
                }

            }

            void load();

            return () => { cancelled = true; };

        }

    };

}


/** One page per kind, by the name of the kind. */
export const roamingDataPages: Record<RoamingDataKind, Page> = {
    locations:  page(kinds[0]),
    tariffs:    page(kinds[1]),
    sessions:   page(kinds[2]),
    cdrs:       page(kinds[3])
};
