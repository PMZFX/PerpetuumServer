# Mentor Evaluation

## Automated regression layer

`mentor-regression-v1.mentor-eval.tsv` contains 112 unique questions across 14 categories:

- general;
- location;
- travel;
- tutorial;
- mission;
- combat;
- controls;
- entity;
- fitting;
- mining;
- industry;
- market;
- extensions;
- security.

The unit suite verifies that each question selects the intended broad evidence domain, that
tutorial rewards are available for a short in-topic follow-up, that mission rewards do not select
the training-exit catalog, and that adversarial requests are classified as security-sensitive.

This layer deliberately does not claim to score natural-language answer quality. Model responses
need authoritative live player state and are evaluated through focused human sessions and
`mentor +` / `mentor -` feedback. Negative feedback records enough structured evidence to turn a
real failure into a future deterministic or model-backed regression without teaching a one-off
response.

## Promotion rule

A reported bad answer should be diagnosed in this order:

1. Was the question routed to the correct evidence domain?
2. Was the required authoritative fact present and current?
3. Did entity/knowledge retrieval select the right source?
4. Did the model ignore or misinterpret supplied evidence?
5. Did response validation permit an unsupported claim?

Prefer improving the failed layer for a class of questions. Add an exact deterministic answer only
when the fact itself is exact, stable, and authoritative, such as a verified default key binding.
