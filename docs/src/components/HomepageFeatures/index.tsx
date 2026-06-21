import type {ReactNode} from 'react';
import clsx from 'clsx';
import Heading from '@theme/Heading';
import styles from './styles.module.css';

type FeatureItem = {
  title: string;
  description: ReactNode;
};

const FeatureList: FeatureItem[] = [
  {
    title: 'Mediator Pattern',
    description: (
      <>
        Decouple your application with Commands, Queries, and Events. Each
        message has a single, focused handler — no hidden dependencies.
      </>
    ),
  },
  {
    title: 'Composable Pipelines',
    description: (
      <>
        Add cross-cutting concerns — validation, logging, retries, metrics —
        as pluggable pipeline stages without touching your handlers.
      </>
    ),
  },
  {
    title: 'Performance First',
    description: (
      <>
        Built for Native AOT and minimal allocations in hot paths.{' '}
        <code>ValueTask</code>-based APIs keep latency low whether you dispatch
        one message or a million.
      </>
    ),
  },
];

function Feature({title, description}: FeatureItem) {
  return (
    <div className={clsx('col col--4')}>
      <div className="text--center padding-horiz--md padding-vert--lg">
        <Heading as="h3">{title}</Heading>
        <p>{description}</p>
      </div>
    </div>
  );
}

export default function HomepageFeatures(): ReactNode {
  return (
    <section className={styles.features}>
      <div className="container">
        <div className="row">
          {FeatureList.map((props, idx) => (
            <Feature key={idx} {...props} />
          ))}
        </div>
      </div>
    </section>
  );
}
