import { auth } from '../auth';
import type { Page } from '@node/router';
import { firstPageOfTheMenu } from '@node/start';

/**
 * Where "/" leads: an operator to the configuration, and anybody else to the
 * first page of the menu they may open - a driver, who may look at nothing of
 * the EMSP but their contracts, to those.
 *
 * Not every node's "/" alone: the contracts come first in the menu, and an
 * operator, who may see everybody's, would land on them rather than on what an
 * operator is here for.
 */
export const homePage: Page = {

    title: 'EMSP',

    render(context) {

        if (auth.can('configuration', 'read'))
            context.navigate('/configuration', true);

        else
            firstPageOfTheMenu.render(context);

    }

};
