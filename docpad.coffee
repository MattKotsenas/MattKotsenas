moment = require('moment')
{ execSync } = require('child_process')

docpadConfig = {
  events:
    generateBefore: (opts, next) ->
      # Generate social preview images before building (dev only, requires sharp)
      try
        require.resolve('sharp')
        console.log('Generating social preview images...')
        execSync('node scripts/generate-social-images.js', { stdio: 'inherit' })
      catch err
        if err.code is 'MODULE_NOT_FOUND'
          console.log('Skipping social image generation (sharp not installed)')
        else
          console.error('Social image generation failed:', err.message)
      next()
  templateData:
    site:
      title: 'void where_prohibited() { ... }'
      tagline: 'Programmer. Runner. Trying my best.'
      description: 'A personal website for Matt Kotsenas'
      logo: '/img/home.png'
      url: '/'
      cover: '/img/cover.jpg'
      baseUrl: 'https://matt.kotsenas.com'
      navigation: [
        {
          name: 'Home',
          href: '/',
          section: 'home'
        },
        {
          name: 'About',
          href: '/about',
          section: 'about'
        },
        {
          name: 'Photos',
          href: '/photos',
          section: 'photos'
        }
      ]
    author:
      name: 'Matt Kotsenas'
      img: 'https://gravatar.com/avatar/13e6e96d176cd18ef232d054b8e65f55'
      url: '/'
      location: 'Seattle, WA',
      bio: ''
    getPreparedTitle: -> if @document.title then "#{@document.title} | #{@site.title}" else @site.title
    getDescription: -> @document.excerpt || @document.description || @site.description
    bodyClass: -> if @document.isPost then "post-template" else "home-template"
    masthead: (d) ->
      d = d || @document
      if d.cover then d.cover else @site.cover
    socialImage: (d) ->
      d = d || @document
      if d.socialImage then d.socialImage else if d.cover then d.cover else @site.cover
    isCurrent: (l) ->
      if @document.section is l.section  then ' nav-current'
      else if @document.url is l.href then ' nav-current'
      else ''
    excerpt: (p,w) ->
      w = w || 26
      if p.excerpt then p.excerpt else p.content.replace(/<%.+%>/gi, '').split(' ').slice(0, w).join(' ')
    encode: (s) -> encodeURIComponent(s)
    slug: (s) -> return s.toLowerCase().replace(' ', '-')
    currentYear: -> new Date().getFullYear()
    time: (ts, format) ->
      format = format || 'MMMM DO, YYYY'
      ts = new Date(ts) || new Date()
      moment(ts).format(format)
  collections:
    posts: ->
      @getCollection("html").findAllLive({active:true, isPost: true, isPagedAuto: {$ne: true}}, {postDate: -1}).on "add", (model) ->
        model.setMetaDefaults({layout:"post"})
  plugins:
    tags:
      extension: '.html'
      injectDocumentHelper: (doc) ->
        doc.setMeta { layout: 'tag' }
    rss:
      default:
        collection: 'posts'
        url: '/rss.xml'
}

module.exports = docpadConfig
