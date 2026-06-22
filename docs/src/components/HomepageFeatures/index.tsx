import type {CSSProperties, ReactNode} from 'react';
import Heading from '@theme/Heading';
import styles from './styles.module.css';

type FeatureItem = {
  icon: string;
  accent: string;
  title: string;
  description: ReactNode;
};

const FeatureList: FeatureItem[] = [
  {
    icon: '⚡',
    accent: '#2e8555',
    title: 'Mediator Pattern',
    description: (
      <>
        Decouple with Commands, Queries, and Events. Each message has one
        focused handler — no hidden dependencies, no service locator.
      </>
    ),
  },
  {
    icon: '🔗',
    accent: '#2563eb',
    title: 'Composable Pipelines',
    description: (
      <>
        Attach validation, logging, retries, and metrics as pluggable pipeline
        behaviors — zero changes to your handlers.
      </>
    ),
  },
  {
    icon: '🚀',
    accent: '#d97706',
    title: 'Performance First',
    description: (
      <>
        <code>ValueTask</code>-based APIs and minimal allocations in hot paths.
        Native AOT support keeps latency low at any message volume.
      </>
    ),
  },
  {
    icon: '🔒',
    accent: '#7c3aed',
    title: 'Zero Runtime Reflection',
    description: (
      <>
        Dispatch delegates compile at DI registration time — no{' '}
        <code>MakeGenericType</code> at request time. Safe for trimming and
        NativeAOT.
      </>
    ),
  },
];

function Feature({icon, accent, title, description}: FeatureItem) {
  return (
    <div
      className={styles.featureCard}
      style={{'--card-accent': accent} as CSSProperties}>
      <div className={styles.featureIconWrap}>
        <span className={styles.featureIcon}>{icon}</span>
      </div>
      <Heading as="h3" className={styles.featureTitle}>{title}</Heading>
      <p className={styles.featureDescription}>{description}</p>
    </div>
  );
}

export default function HomepageFeatures(): ReactNode {
  return (
    <section className={styles.features}>
      <div className="container">
        <Heading as="h2" className={styles.sectionHeading}>Why Synapse?</Heading>
        <div className={styles.featuresGrid}>
          {FeatureList.map((props, idx) => (
            <Feature key={idx} {...props} />
          ))}
        </div>
      </div>
    </section>
  );
}
