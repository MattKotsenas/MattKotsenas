---
title: "The Control Paradox"
cover: /img/the-control-paradox/cover.jpg
isPost: true
active: true
excerpt: Your agent orchestrator probably gives you less control than you think.
postDate: '2026-02-04 15:22:59 GMT-0800'
tags:
 - running
 - race reports
---

Your agent orchestrator probably gives you less control than you think.

## The setup

Here's a scene that plays out in engineering organizations everywhere: a team decides to use AI agents for large-scale
refactoring across their codebase. Hundreds of repos. Thousands of files. The kind of work that would take a human team
years.

Let's call this kind of cross-repo effort a _campaign_: a named initiative with a defined scope, tracked state, and a
finish line.

And then someone says the magic words: "we need a pipeline."

Of course we do! We're engineers. We build pipelines. We've been building pipelines since Jenkins was just a glimmer in
Kohsuke Kawaguchi's eye. Clone, build, test, transform, verify, PR. Steps. States. Transitions. This is our comfort zone.

So the team goes off and builds what I'll call the **Narrow Context Pattern**: a deterministic state machine that drives
an AI agent through discrete steps. The agent gets called at specific points with prompts like "fix this build error"
or "migrate this API call", but the orchestrator holds the reins. The agent sees only what we show it. The pipeline
controls the flow.

It feels safe. It feels _engineered_.

And here's the paradox: **by taking control away from the agent, we often get worse outcomes**.

## Iron Man vs. Ultron (but not how you'd expect)

Tom Limoncelli wrote a fantastic piece called [Automation Should Be Like Iron Man, Not Ultron][iron-man-vs-ultron].
The thesis is essentially that automation should collaborate with humans, not replace them. Ultron-style automation runs
autonomously until it hits a wall, then fails catastrophically. Iron Man-style automation keeps humans in the loop,
empowering them to do more while staying in control.

Most people read this and think: "Right, so we should constrain our AI agents. Keep them on a leash so we understand
what they're doing. Pipeline-driven orchestration = Iron Man."

**They have it exactly backwards.**

The Narrow Context Pattern is Ultron in disguise.

When the pipeline fails at step 47 of 52, what are your options? You can retry. You can restart from scratch. You can...
that's basically it. The agent has no memory of why it made decisions in steps 1-46. The orchestrator has state, such as
outputs and completed steps, but the *reasoning* is gone. The agent was a hired gun, called in for specific jobs,
dismissed after each one.

There's no collaboration. There's no graceful recovery. There's just "try again."

Meanwhile, an agent that understands the overall goal, has visibility into its own history, and can reason about the
entire campaign can say "step 47 failed because of a decision I made in step 12, let me reconsider." It can collaborate
with a human who drops in to help. It can *adapt*.

That's Iron Man. The suit enhances Tony Stark's judgment. It doesn't replace it with a rigid flowchart.

## The complexity cliff

I've taken to calling the failure mode implied by Limoncelli's thinking the **complexity cliff**: automation becomes
binary. Either it works, or it doesn't. No middle ground. No partial success.

This is important because it changes how you reach for the tool. Before you run it, you have to estimate the probability
of success. If that probability is low, you don't bother. The cliff limits usefulness before you even start.

Pipeline orchestrators hit this cliff hard. Not because they're poorly built. Often they're beautifully engineered, with
retries and caching and telemetry. Nonetheless they fail because their fundamental model doesn't scale to genuine
novelty.

Consider: you've built a migration pipeline that handles 90% of cases elegantly. The remaining 10% are edge cases. So
you add conditionals. Exception handlers. Special-case steps. Your clean state machine accumulates complexity like
sediment.

Now something truly novel happens. A repo with a structure no one anticipated. A build system that violates assumptions.

Your Ultron falls off the cliff. The agent that made the decisions is long gone. Its context evaporated after each step
completed. And by Limoncelli's Leftover Principle, because this happens rarely, engineers don't know what to do. They
stare at logs trying to reconstruct what happened, quickly give up, and do the work by hand. 

Early in our experimentation, we built exactly this kind of system. It worked at small scale:a handful of repos, simple
transformations. We thought we were being rigorous. We thought deterministic control meant predictable outcomes. What we
discovered was that we'd drawn the box too big around the orchestrator and too small around the agent. At scale, the
narrow context pattern produced _locally reasonable but globally suboptimal decisions_. The agent would try to do
exactly what we asked in each step. However, without visibility into the overall goal it couldn't course-correct. It
couldn't reuse use prior investigation. It re-reasoned about how to build each time. Small issues compound and
frequently the agent fails or gives up.

## Persistent task tracking

So what's the alternative? Let the agent run wild? Full autonomy, hope for the best?

No. That way lies what some have affectionately called "agent chaos at scale". In this world, pure autonomy works if you
have senior engineers with deep pockets who can vibe-code their way through the wreckage. It doesn't work for production
systems that need rigorous engineering.

The answer is an emerging pattern that others have arrived at independently: **let the agent decompose its own work, but
persist that decomposition outside the context window**. [Beads][beads-github] encodes tasks as a dependency-aware DAG
of issues, stored in Git-backed JSON. The agent can easily query "what's ready to work on". The
[Ralph Loop][ralph-technique] takes a complementary approach: spawn a fresh agent instance per iteration, with state
persisted to filesystem and git. Each agent reads the current state, does a unit of work, updates the state, and
terminates. No context rot. No compounding errors from long conversations.

What these patterns share:
- **Agent owns work breakdown**: The agent decomposes goals into tasks, not externally imposed by an orchestrator
- **Persistent tracking outside context**: Task state lives on outside chat history
- **Goal visibility**: The agent can always see the overall objective and remaining work and giving it a trajectory
- **Explore, then preserve**: The agent can fork, experiment, and backtrack with only the chosen path gets persisted.
    This works better than simple context compaction for reasons that aren't entirely clear to me.

## Isn't this where we started?

At first glance, it might sound like we've come full circle to the Narrow Context Pattern I was criticizing to begin
with. Both involve discrete units of work. Both persist state externally. But the key difference is _who's driving_.

- **Narrow Context Pattern**: External orchestrator drives the agent, feeding it one task at a time. The agent focuses
    because it literally cannot see anything else.
- **Persistent Task Tracking**: The agent maintains visibility into the overall goal while working on specific tasks. It
    focuses because it *chooses* to, with the full campaign in view.

Same practical effect (focus on immediate work), completely different reasoning dynamics.

We discovered this need through trial and error. Before adopting persistent task tracking, the agent would wander
aimlessly and make "carless mistakes" like cloning repos to different locations between iterations, losing track of its
work, etc. Giving it an external driver didn't help either; the agent would reason correctly on each narrow step, but
lose that reasoning on the next iteration. Each restart compounded failure risk because context was lost. When we gave
the agent persistent task tracking with the overall goal visible, something shifted. It stopped wandering. It could
reason about whether its current action served the larger purpose.

Persistent task tracking also enables the Iron Man collaboration we actually want:
- A human can see what the agent is working on
- A human can resume a failed campaign with context intact
- A human can adjust goals mid-flight without starting over
- Multiple agents or humans can collaborate on the same campaign

When you hit the complexity cliff—and you will—Iron Man can fly. Ultron just crashes.

![Agent kanban board][kanban-board]

## Where to Draw the Line

I don't know exactly where the line should be drawn between agent autonomy and deterministic control. Nobody does yet. But I'm increasingly convinced it's **closer to the agent-native end than most engineers assume**.

Here's a framework for thinking about it:

### Give to the Agent:
- **Goal visibility**: The agent should always know *why* it's doing what it's doing
- **Decision authority over approach**: If there are multiple ways to accomplish a task, let the agent choose
- **Recovery and adaptation**: When things go wrong, the agent should be first responder
- **Context accumulation**: History of decisions, not just history of outputs

**DO**: Let the agent own everything inside the sandbox—filesystem, local git operations, build, test, approach decisions, own work breakdown, retry logic, marking tasks blocked.

**DON'T**: Strip ambient state by driving the agent externally step-by-step.

**DON'T**: Hide what tasks remain or what's been done. The agent needs that visibility.

### Keep in Deterministic Control:
- **Irreversible external effects**: Opening PRs, merging code, modifying production systems
- **Concurrency boundaries**: Which repos are being worked on, lease management, preventing conflicts
- **Audit trail**: What happened, when, and what state resulted (but not *how* the agent reasoned)
- **Resource limits**: Timeouts, token budgets, iteration caps

**DO**: Use a harness as a safety net for side effects—a broker for push/PR, a policy layer for campaign-level mutations (add/remove repos, abandon tasks, halt campaign), meta-recovery (retry with different model), and resource enforcement.

**DON'T**: Let the agent mutate campaign scope. It can mark tasks blocked; only the harness can abandon them. The agent proposes, the harness disposes.

### The Risk Framing That Changes Everything

Here's what makes agent orchestration fundamentally different from the ops automation that Limoncelli was writing about: **before opening a PR, mistakes are cheap**.

We're not doing ops work. Nothing is unrecoverable. If the agent clones a repo wrong, clone again. If it generates bad code, regenerate. If it goes down a blind alley, backtrack.

The entire pre-PR phase is a sandbox. We can afford way more agent autonomy than our ops-trained instincts suggest. The place to be rigorous is at the gate—the moment of irreversible effect—not in the exploratory work that precedes it.

## Lessons Learned

Some things we learned the hard way:

### On Focus
- Agents get "bored" with parallelism. I'm not anthropomorphizing here—when we asked an agent why it skipped steps during parallel work, that was its own rationale. Give them too many concurrent tasks and they start cutting corners. Sequential focus works better than you'd think for keeping the work coherent.
- External task tracking (visible to the agent) is remarkably effective. It provides a vector to reason against that prevents drift.
- The narrow context pattern achieves focus by restriction. Persistent task tracking achieves focus by goal-visibility. The latter produces better decisions.

### On Collaboration
- A campaign ledger—a persistent record of what's been done and what's pending—enables collaboration in ways stateless pipelines cannot.
- When a human can see agent state and resume from where it left off, the whole system becomes more robust. Failures become pauses, not restarts.
- Claude Code / Copilot plugins are underrated for collaboration. They let teams share capabilities across workflows, enable reuse between engineers, and—critically—bridge the gap between "works on my machine" and "works at campaign scale." That bridge shrinks the complexity cliff: if you can test a plugin locally on one repo, you can trust it across hundreds.

### On Debugging
- Debug logs make agent behavior auditable. But there's a difference between auditable (what happened) and interpretable (why it happened).
- LLM-as-judge patterns are useful for evaluating outcomes. Have a separate agent analyze the work, reflecting the original prompt back to assess alignment.
- When the agent fails consistently, feeding it debug logs and asking "what went wrong" produces surprisingly useful meta-analysis.

### On Nondeterminism
- We used to worry a lot about agent nondeterminism. What we discovered is that it matters less than we thought for exploratory work, and matters enormously for reproducible testing. The solution isn't making agents deterministic—it's accepting nondeterminism for execution while maintaining deterministic test harnesses.
- An LLM pretending to be a state machine is fine, as long as you don't *need* it to be a state machine.

### On Cost
- The economics have shifted. At ~$0.08/iteration for meaningful work, the cost of agent orchestration is noise compared to developer time.
- The real cost isn't tokens—it's iteration time when things go wrong. Narrow context makes iteration expensive by destroying recoverability.

## The Position

Most engineers building agent orchestrators default to deterministic control because that's how we've always built automation. We extend our mental models from CI pipelines and workflow engines. This isn't distrust of AI—it's the natural path of least cognitive resistance.

But the Narrow Context Pattern carries hidden costs:
- Agents make locally reasonable but globally suboptimal decisions
- Recovery from failure means restart, not resume
- The complexity cliff looms as edge cases accumulate
- We build Ultron thinking we're building Iron Man

Persistent task tracking offers an alternative: give agents goal visibility and a vector to reason against while keeping deterministic control over irreversible effects. Focus through purpose, not through blindness.

I don't know exactly where the line belongs. But I'm convinced it's closer to the agent-native end than most people think.

The control paradox: sometimes you get more control by giving it away.

## What Now

### If you're authoring campaigns

**Start with sandboxing.** Think about isolation early—it's painful to retrofit. Use the built-in extension points where possible. PreToolUse hooks can prevent side effects without the pain of full containerization.

**Adopt persistent task tracking.** Don't reinvent this. Beads and Ralph Loop exist. Pick one or build something similar, but get task state out of the chat history and into something durable. This is how you get resume instead of restart.

**Resist the urge to parallelize everything.** Serial focus beats parallel chaos. Your agent will thank you (or at least stop skipping steps).

**Remember: pre-PR mistakes are cheap.** Don't over-engineer the exploratory phase. Save your rigor for the gates—the moments of irreversible effect.

**Use LLM-as-judge for evaluation.** When campaigns finish (or fail), have a separate agent analyze the work. Feed it the original prompt and ask "did this accomplish the goal?" The meta-analysis is surprisingly useful.

**Separate tool brand from campaign brand.** If you're running campaigns for many engineers, one poorly-conceived campaign can teach your users that your tool produces slop. Make sure they know the difference between "this campaign was bad" and "this tool is bad."

### If you're building plugins or skills

**Design for testability in isolation.** A plugin tested locally on one repo should work across hundreds. That's the whole point—bridging the complexity cliff between your machine and campaign scale.

**Think about composability.** Skills that work well together multiply value. Skills that fight each other multiply debugging time.

**Document the contract.** What does your plugin expect? What does it produce? Other engineers (and agents) will thank you.

### If you're building agent tooling

**Allow hooks to mutate, not just observe.** Claude Code gets this right—PreToolUse can rewrite commands, UserPromptSubmit can inject context. Example: automatically adding `--reference-if-able` to git clone commands to use a local cache. Copilot and others should match this capability.

**Allow hooks on incoming and outgoing messages.** Full prompt/response interception enables guardrails, context injection, and policy enforcement without forking the agent.

**Consider a "hermetic mode."** A profile that specifies: these custom instructions, these tools, these plugins—and *only* these. Everything listed is automatically installed. Everything not listed is automatically removed (from both repo and user locations). Reproducibility matters for campaigns.

**Expose task state as a first-class primitive.** Not chat history—actual structured task state. Make it easy for agents to know what's done, what's pending, and what's blocked. Make it easy for humans to see the same thing.

**Make goal visibility easy to maintain.** When context compacts or the agent spawns fresh, the overall objective shouldn't evaporate. This is the difference between Iron Man and Ultron.

---

## References

- Limoncelli, Thomas A. "Automation Should Be Like Iron Man, Not Ultron." *ACM Queue*, October 2015.
- Yegge, Steve. "Beads: A Git-Friendly Issue Tracker for AI Coding Agents." GitHub, 2025.
- snarktank. "Ralph: An Autonomous AI Agent Loop." GitHub, 2025.

[iron-man-vs-ultron]: https://queue.acm.org/detail.cfm?id=2841313
[beads-github]: https://github.com/steveyegge/beads
[ralph-technique]: https://www.humanlayer.dev/blog/brief-history-of-ralph
[kanban-board]: /img/the-control-paradox/kanban.png