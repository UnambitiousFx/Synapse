import type {SidebarsConfig} from '@docusaurus/plugin-content-docs';

const sidebars: SidebarsConfig = {
  tutorialSidebar: [
    'index',
    {
      type: 'category',
      label: 'Getting Started',
      collapsed: false,
      items: ['getting-started'],
    },
    {
      type: 'category',
      label: 'Core Concepts',
      collapsed: false,
      items: ['commands-and-queries', 'events', 'streaming', 'context', 'propagation'],
    },
    {
      type: 'category',
      label: 'Pipeline & Behaviors',
      items: ['pipelines', 'validation', 'error-handling'],
    },
    {
      type: 'category',
      label: 'Advanced & Integration',
      items: ['outbox', 'aspnetcore', 'source-generator', 'observability'],
    },
    {
      type: 'category',
      label: 'Endpoints',
      link: {type: 'doc', id: 'endpoints/index'},
      items: [
        'endpoints/quickstart',
        {
          type: 'category',
          label: 'High level',
          items: [
            'endpoints/high-level/overview',
            'endpoints/high-level/messages',
            'endpoints/high-level/responses',
            'endpoints/high-level/groups',
            'endpoints/high-level/streaming',
          ],
        },
        {
          type: 'category',
          label: 'Low level',
          items: [
            'endpoints/low-level/overview',
            'endpoints/low-level/reading-the-request',
            'endpoints/low-level/validating',
            'endpoints/low-level/mediator-bound',
          ],
        },
        {
          type: 'category',
          label: 'Reference',
          items: [
            'endpoints/reference/base-classes',
            'endpoints/reference/openapi',
            'endpoints/reference/native-aot',
            'endpoints/reference/diagnostics',
            'endpoints/reference/escape-hatches',
          ],
        },
      ],
    },
    {
      type: 'category',
      label: 'Performance',
      items: ['benchmarks'],
    },
    {
      type: 'category',
      label: 'Project',
      items: ['examples', 'changelog', 'using-with-ai', 'migration-v2'],
    },
  ],
};

export default sidebars;
