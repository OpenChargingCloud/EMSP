import { auth } from '../auth';
import type { Page } from '@node/router';

/**
 * Where "/" leads: an operator to the configuration, a driver - who may look
 * at nothing of the EMSP but their contracts - to those.
 *
 * A page that renders nothing and navigates on, with the address replaced, so
 * that the back button does not come here again.
 */
export const homePage: Page = {

    title: 'EMSP',

    render({ navigate }) {
        navigate(auth.can('configuration', 'read') ? '/configuration' : '/contracts', true);
    }

};
