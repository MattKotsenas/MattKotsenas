---
title: "Running docpad on Azure in 2015"
isPost: true
active: true
excerpt: I really needed to freshen up the site. It was created hastily and really didn't follow best practices...
postDate: '2015-12-23 06:19:19 GMT-0800'
tags:
 - docpad
 - azure
---

## Why docpad

I really needed to freshen up the site. It was created hastily and really didn't follow best practices. I wanted something that would reflect better on me and that I wouldn't be embarrassed to direct people to. So I started evaluating my options against these criteria:

- **Looks good** - I'm not a designer; I'm that jerk who forgets to clear floats. I want something with themes / templates / layouts I can follow to avoid hurting others' eyes.

- **Supports dev and ops best practices** - Most of my experience is in backend development, and I have only passing familiarity with many frontend development tools and practices from the last few years. I'd love to use the site as a playground to learn production-quality processes and tools (Grunt, Less, Bower, etc.).

- **Works with Azure** - I've already got some other projects in Azure and I really enjoy using it. So my solution should run in Azure, and per the previous requirement, with minimal bubble gum and shoestring.

After looking at that list and thinking about where the site is likely to go in the next year or two I started leaning towards [static site generators](https://davidwalsh.name/introduction-static-site-generators) because they keep costs low, support good practices and performance, and are an opportunity to learn more tools.

My first choice for a site generator was [Jekyll](https://jekyllrb.com/). It's popular, well supported, and fits *most* my requirements. However, Azure websites don't natively support ruby, and I didn't want to check in the generated site because committing generated files feels like a violation of dev best practices. After spending some time looking for alternatives, [docpad](http://docpad.org/) seemed like a great fit since it seemed relatively popular, had "skeletons" which function as themes, and ran on node which Azure supports.

## Setup

Chances are if you found this blog post you're probably also having problems deploying docpad on Azure. Before continuing, be sure to read the [official deploy docs](http://docpad.org/docs/deploy/) and [Nathan's setup guide](https://github.com/ntotten/ntotten.github.com/blob/master/_posts/2013-01-11-static-site-generation-with-docpad-on-windows-azure-web-sites.md). I don't want to repeat the great content that's already there, just provide some help for specific issues you may encounter.

### process.stdin errors

After following the instructions I'd get the following error in the deployment logs

```bash
Error: The task [generate] just completed, but it had already completed earlier, this is unexpected. State information is:
{ error: 'Error: The task [actions bundle: load ready ?  ready] just completed, but it had already completed earlier, this is unexpected. State information is:
{ error: \'Error: EINVAL, invalid argument
  at new Socket (net.js:156:18)
    at process.stdin (node.js:664:19)
      at ConsoleInterface.destroy (D:\home\site\repository\node_modules\docpad\out\lib\interfaces\console.js:111:14)
        at ConsoleInterface.destroy (D:\home\site\repository\node_modules\docpad\out\lib\interfaces\console.js:4:59)
          at completeAction (D:\home\site\repository\node_modules\docpad\out\lib\interfaces\console.js:166:35)
            at Task.

...<snip>...

  at Task.exit (D:\home\site\repository\node_modules\docpad\node_modules\taskgroup\out\lib\taskgroup.js:309:15)
  at Domain.EventEmitter.emit (events.js:95:17)
  at process._fatalException (node.js:249:27)
  at process._fatalException (node.js:267:30)
  at process._fatalException (node.js:267:30)
```

Searching around I see StackOverflow answers like [this](http://stackoverflow.com/questions/17297422/trouble-with-starting-node-js-from-a-cygwin-console) which say cygwin isn't supported by node. I'm not certain that Azure uses cygwin, but I do know they support bash scripts in Windows environments, so it's possible they're running the node scripts under that environment.

If you keep digging you'll see [recommendations](http://stackoverflow.com/a/28553596) to use Grunt with a plugin like `grunt-shell` or `grunt-exec` to run docpad and turn stdin off to avoid the issue. Don't do that (you'll see why). Instead, in your `package.json` change your node engine from 0.12 like they suggest in the docs to 5.0.0.

### docpad hangs when running under Grunt

Before upgrading my node version I first tried running docpad under Grunt to avoid the aforementioned stdin issue, which ran into [this hang](https://github.com/docpad/docpad/issues/988). The only workaround I've found is to downgrade docpad from 6.78.4 to 6.75.x.

### Error: shutdown EPIPE

After getting my stdin issue resolved by using a newer version of node, I tried upgrading to the latest docpad version again. At that point I started getting the following stack:

```bash
events.js:141
      throw er; // Unhandled 'error' event
            ^
Error: shutdown EPIPE
    at exports._errnoException (util.js:734:11)
    at ReadStream.onSocketFinish (net.js:218:26)
    at emitNone (events.js:67:13)
    at ReadStream.emit (events.js:163:7)
    at finishMaybe (_stream_writable.js:477:14)
    at endWritable (_stream_writable.js:486:3)
    at ReadStream.Writable.end (_stream_writable.js:452:5)
    at ReadStream.Socket.end (net.js:393:31)
    at process._tickCallback (node.js:350:11)
```

I'm not sure what caused this, but downgrading docpad to 6.75.x again fixed the issue.

## Wrap Up

It seems that node has been moving fast over the last few years and the ecosystem is still catching up. I'll update this post with additional errors and bug numbers as I find and file them. If I'm missing something feel free to reach out to [@MattKotsenas](https://twitter.com/MattKotsenas)!
