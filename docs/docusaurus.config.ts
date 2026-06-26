import {themes as prismThemes} from 'prism-react-renderer';
import type {Config} from '@docusaurus/types';
import type * as Preset from '@docusaurus/preset-classic';

// This runs in Node.js - Don't use client-side code here (browser APIs, JSX...)

const config: Config = {
  title: 'Synapse - UnambitiousFx',
  tagline: 'Synapse is a simple Native-AOT mediator',
  favicon: 'img/favicon.ico',

  // Future flags, see https://docusaurus.io/docs/api/docusaurus-config#future
  future: {
    v4: true, // Improve compatibility with the upcoming Docusaurus v4
  },

  // Set the production url of your site here
  url: 'https://synapse.unambitiousfx.com',
  // Set the /<baseUrl>/ pathname under which your site is served
  baseUrl: '/',

  // GitHub pages deployment config.
  organizationName: 'UnambitiousFx',
  projectName: 'Synapse',

  plugins: [
    [
      'docusaurus-plugin-llms',
      {
        generateLLMsTxt: true,
        generateLLMsFullTxt: true,
        excludeImports: true,
        removeDuplicateHeadings: true,
        // Lead the index with the natural reading path; everything else follows.
        includeOrder: [
          'index.mdx',
          'getting-started.mdx',
          'commands-and-queries.mdx',
          'events.mdx',
          'pipelines.mdx',
        ],
        includeUnmatchedLast: true,
        // Resolved/known issues are noise for an AI reading the API surface.
        ignoreFiles: ['known-issues/**'],
        // Focused bundles so an assistant can pull one concern, not the whole dump.
        customLLMFiles: [
          {
            filename: 'llms-pipelines.txt',
            includePatterns: ['pipelines.mdx', 'validation.mdx', 'error-handling.mdx'],
            fullContent: true,
            title: 'Synapse Pipelines & Behaviors',
            description: 'Pipeline behaviors, validation, and error handling.',
          },
          {
            filename: 'llms-outbox.txt',
            includePatterns: ['outbox.mdx', 'events.mdx', 'observability.mdx'],
            fullContent: true,
            title: 'Synapse Events, Outbox & Observability',
            description: 'Event publishing, the outbox pattern, and observability.',
          },
        ],
      },
    ],
  ],

  themes: ['@docusaurus/theme-mermaid'],

  markdown: {
    mermaid: true,
  },

  onBrokenLinks: 'throw',

  // Even if you don't use internationalization, you can use this field to set
  // useful metadata like html lang. For example, if your site is Chinese, you
  // may want to replace "en" with "zh-Hans".
  i18n: {
    defaultLocale: 'en',
    locales: ['en'],
  },

  presets: [
    [
      'classic',
      {
        docs: {
          sidebarPath: './sidebars.ts',
          // Please change this to your repo.
          // Remove this to remove the "edit this page" links.
          editUrl:
            'https://github.com/UnambitiousFx/Synapse/',
        },
        blog: false,
        theme: {
          customCss: './src/css/custom.css',
        },
      } satisfies Preset.Options,
    ],
  ],

  themeConfig: {
    // Replace with your project's social card
    image: 'img/docusaurus-social-card.jpg',
    colorMode: {
      respectPrefersColorScheme: true,
    },
    navbar: {
      title: 'Synapse',
      logo: {
        alt: 'Synapse Logo',
        src: 'img/logo.svg',
      },
      items: [
        {
          type: 'docSidebar',
          sidebarId: 'tutorialSidebar',
          position: 'left',
          label: 'Docs',
        },
        {
          type: 'doc',
          docId: 'commands-and-queries',
          position: 'left',
          label: 'Commands & Queries',
        },
        {
          type: 'doc',
          docId: 'pipelines',
          position: 'left',
          label: 'Pipelines',
        },
        {
          type: 'doc',
          docId: 'aspnetcore',
          position: 'left',
          label: 'ASP.NET Core',
        },
        {
          href: 'https://www.nuget.org/packages?q=UnambitiousFx.Synapse',
          label: 'NuGet',
          position: 'right',
        },
        {
          href: 'https://github.com/UnambitiousFx/Synapse',
          label: 'GitHub',
          position: 'right',
        },
      ],
    },
    footer: {
      style: 'dark',
      links: [
        {
          title: 'Docs',
          items: [
            {
              label: 'Getting Started',
              to: '/docs/getting-started',
            },
            {
              label: 'Commands & Queries',
              to: '/docs/commands-and-queries',
            },
            {
              label: 'Pipeline Behaviors',
              to: '/docs/pipelines',
            },
            {
              label: 'ASP.NET Core',
              to: '/docs/aspnetcore',
            },
          ],
        },
        {
          title: 'More',
          items: [
            {
              label: 'GitHub',
              href: 'https://github.com/UnambitiousFx/Synapse',
            },
            {
              label: 'NuGet',
              href: 'https://www.nuget.org/packages?q=UnambitiousFx.Synapse',
            },
          ],
        },
      ],
      copyright: `Copyright © ${new Date().getFullYear()} UnambitiousFx. Built with Docusaurus.`,
    },
    prism: {
      theme: prismThemes.github,
      darkTheme: prismThemes.dracula,
    },
  } satisfies Preset.ThemeConfig,
};

export default config;
