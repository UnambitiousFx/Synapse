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
      items: ['commands-and-queries', 'events', 'streaming', 'context'],
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
  ],
};

export default sidebars;
