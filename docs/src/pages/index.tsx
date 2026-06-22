import type {ReactNode} from 'react';
import clsx from 'clsx';
import Link from '@docusaurus/Link';
import useDocusaurusContext from '@docusaurus/useDocusaurusContext';
import Layout from '@theme/Layout';
import Heading from '@theme/Heading';
import HomepageFeatures from '@site/src/components/HomepageFeatures';

import styles from './index.module.css';

const QUICK_START = `// 1 · Register
services.AddSynapse(cfg =>
    cfg.RegisterRequestHandler<
        CreateTaskHandler,
        CreateTaskCommand, Guid>());

// 2 · Define
public record CreateTaskCommand(string Title)
    : IRequest<Guid>;

public class CreateTaskHandler
    : IRequestHandler<CreateTaskCommand, Guid>
{
    public ValueTask<Result<Guid>> HandleAsync(
        CreateTaskCommand cmd, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        return ValueTask.FromResult(Result.Success(id));
    }
}

// 3 · Invoke
var result = await invoker.InvokeAsync(
    new CreateTaskCommand("Buy milk"), ct);

result.Match(
    success: id    => Ok(id),
    failure: error => BadRequest(error.ToString()));`;

function HomepageHeader() {
  const {siteConfig} = useDocusaurusContext();
  return (
    <header className={styles.heroBanner}>
      <div className={clsx('container', styles.heroInner)}>
        <div className={styles.heroLeft}>
          <Heading as="h1" className="hero__title" style={{color: 'inherit', margin: 0}}>
            {siteConfig.title}
          </Heading>
          <p className={styles.heroTagline}>{siteConfig.tagline}</p>
          <div className={styles.heroButtons}>
            <Link
              className="button button--primary button--lg"
              to="/docs/getting-started">
              Get Started →
            </Link>
            <Link
              className="button button--outline button--lg"
              style={{color: 'inherit', borderColor: 'rgba(255,255,255,0.4)'}}
              href="https://github.com/UnambitiousFx/Synapse">
              View on GitHub
            </Link>
          </div>
          <div className={styles.heroInstall}>
            <code>dotnet add package UnambitiousFx.Synapse</code>
          </div>
        </div>

        <div className={styles.heroCode}>
          <div className={styles.heroCodeLabel}>Quick start</div>
          <pre>{QUICK_START}</pre>
        </div>
      </div>
    </header>
  );
}

type NextStepItem = {
  emoji: string;
  title: string;
  description: string;
  to: string;
};

const NEXT_STEPS: NextStepItem[] = [
  {
    emoji: '🚀',
    title: 'Getting Started',
    description: 'Install, register handlers, and send your first command.',
    to: '/docs/getting-started',
  },
  {
    emoji: '📨',
    title: 'Core Concepts',
    description: 'Commands, Queries, Events, Streaming, and Context.',
    to: '/docs/commands-and-queries',
  },
  {
    emoji: '🔗',
    title: 'Pipeline Behaviors',
    description: 'Validation, logging, retries — composable and testable.',
    to: '/docs/pipelines',
  },
  {
    emoji: '🌐',
    title: 'ASP.NET Core',
    description: 'HTTP invoker, MVC integration, and correlation middleware.',
    to: '/docs/aspnetcore',
  },
];

export default function Home(): ReactNode {
  const {siteConfig} = useDocusaurusContext();
  return (
    <Layout
      title={siteConfig.title}
      description="Lightweight, Native-AOT-ready in-process mediator for .NET. Commands, Queries, Events, and composable Pipelines with zero runtime reflection.">
      <HomepageHeader />
      <main>
        <HomepageFeatures />
        <section className={styles.nextSteps}>
          <div className="container">
            <Heading as="h2" className="text--center">Explore the docs</Heading>
            <div className={styles.nextStepsGrid}>
              {NEXT_STEPS.map((item) => (
                <Link key={item.to} to={item.to} style={{textDecoration: 'none'}}>
                  <div
                    style={{
                      background: 'var(--synapse-card-bg)',
                      border: '1px solid var(--synapse-card-border)',
                      borderRadius: '10px',
                      padding: '1.25rem',
                      height: '100%',
                      transition: 'box-shadow 0.2s, transform 0.2s',
                    }}
                    onMouseEnter={e => {
                      (e.currentTarget as HTMLDivElement).style.transform = 'translateY(-2px)';
                      (e.currentTarget as HTMLDivElement).style.boxShadow = 'var(--synapse-card-shadow-hover)';
                    }}
                    onMouseLeave={e => {
                      (e.currentTarget as HTMLDivElement).style.transform = '';
                      (e.currentTarget as HTMLDivElement).style.boxShadow = '';
                    }}>
                    <div style={{fontSize: '1.75rem', marginBottom: '0.5rem'}}>{item.emoji}</div>
                    <Heading as="h3" style={{fontSize: '1rem', marginBottom: '0.4rem'}}>
                      {item.title}
                    </Heading>
                    <p style={{fontSize: '0.875rem', margin: 0, opacity: 0.75}}>{item.description}</p>
                  </div>
                </Link>
              ))}
            </div>
          </div>
        </section>
      </main>
    </Layout>
  );
}
