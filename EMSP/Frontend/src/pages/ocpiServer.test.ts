/**
 * The page of the CPOs that connect drawn, in a document of happy-dom,
 * against a stand-in EMSP: the WebSocket server of OCPI as it is and who is
 * connected - and nobody, where nobody connected yet.
 */

import { open, type Asked } from '../../test/controller.ts';

import { strict as assert }  from 'node:assert';
import { describe, it }      from 'node:test';

const { ocpiServerPage } = await import('./ocpiServer.ts');


function server(connected: string[], running = true): Record<string, unknown> {
    return {
        configuration:  { enabled: true, port: 2359, clientAuthentication: [ 'httpAuthOverTLS', 'tlsClientCertificate' ], ssh: { enabled: true } },
        state:          { running, tls: true, ssh: running, sshPort: 22 },
        connected
    };
}

function answering(Server: Record<string, unknown>) {
    return (asked: Asked) => asked.path === '/configuration/ocpi-server' ? Server : undefined;
}


describe('the page of the CPOs that connect', () => {

    it('says the server is listening, and who is connected', async () => {

        await open(ocpiServerPage, '/configuration/ocpi-server', [ 'partners:read' ],
                   answering(server([ 'cpo-01', 'cpo-02' ])), root => root.querySelector('#ocpi-connected') !== null);

        assert.match(document.querySelector('#ocpi-websocket')?.textContent ?? '', /listening on port 2359, over TLS/);
        assert.match(document.querySelector('#ocpi-ssh')?.textContent       ?? '', /served on port 22/);

        const connected = [ ...document.querySelectorAll('#ocpi-connected li') ].map(item => item.textContent?.trim());

        assert.deepEqual(connected, [ 'cpo-01', 'cpo-02' ]);

    });

    it('says so, where the server does not listen and nobody is connected', async () => {

        await open(ocpiServerPage, '/configuration/ocpi-server', [ 'partners:read' ],
                   answering(server([], false)), root => root.querySelector('#ocpi-connected') !== null);

        assert.match(document.querySelector('#ocpi-websocket')?.textContent ?? '', /switched on, not listening/);
        assert.match(document.querySelector('#ocpi-ssh')?.textContent       ?? '', /not served/);
        assert.match(document.querySelector('#ocpi-connected')?.textContent ?? '', /No CPO is connected/);

    });

});
