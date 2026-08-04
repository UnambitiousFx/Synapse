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
