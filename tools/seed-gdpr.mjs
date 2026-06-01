// GDPR seeder — populates docs-samples/gdpr/ from the official article texts
// published on gdpr-info.eu (Regulation (EU) 2016/679).
//
// Usage:  node tools/seed-gdpr.mjs
// Fetches each article page, extracts its text, and writes one .md per article
// — ready for /ingest. Each article is a natural, citable chunk.

import { writeFile, mkdir, readdir, unlink } from 'node:fs/promises'
import { join, dirname } from 'node:path'
import { fileURLToPath } from 'node:url'

const TOTAL_ARTICLES = 99
const __dirname = dirname(fileURLToPath(import.meta.url))
const OUT = join(__dirname, '..', 'docs-samples', 'gdpr')

function decodeEntities(s) {
  return s
    .replace(/&#8217;|&#8216;|&#8242;/g, "'")
    .replace(/&#8220;|&#8221;/g, '"')
    .replace(/&#8211;|&#8212;/g, '-')
    .replace(/&nbsp;/gi, ' ')
    .replace(/&amp;/gi, '&')
    .replace(/&lt;/gi, '<')
    .replace(/&gt;/gi, '>')
    .replace(/&quot;/gi, '"')
    .replace(/&#(\d+);/g, (_, n) => String.fromCharCode(Number(n)))
}

function stripTags(html) {
  return decodeEntities(
    html
      .replace(/<li[^>]*>/gi, '\n')
      .replace(/<\/(p|div|h\d|tr)>|<br\s*\/?>/gi, '\n')
      .replace(/<[^>]+>/g, ' '),
  )
    .replace(/[ \t]+/g, ' ')
    .split('\n')
    .map((l) => l.trim())
    .filter(Boolean)
    .join('\n')
}

async function fetchArticle(n) {
  const url = `https://gdpr-info.eu/art-${n}-gdpr/`
  const res = await fetch(url, { headers: { 'User-Agent': 'Mozilla/5.0' } })
  if (!res.ok) throw new Error(`HTTP ${res.status} for article ${n}`)
  const html = await res.text()

  // Title from the <h1> (e.g. "Art. 7 GDPR Conditions for consent").
  const h1 = html.match(/<h1[^>]*>([\s\S]*?)<\/h1>/i)
  const title = h1
    ? h1[1].replace(/<[^>]+>/g, '').replace(/\s+/g, ' ').replace(/\bGDPR\b/, '—').trim()
    : `Art. ${n}`

  // Body from the entry-content area, cut before the "Suitable Recitals" block.
  const start = html.search(/<div[^>]*class="[^"]*entry-content/i)
  let body = stripTags(html.slice(start, start + 14000))
  body = body.split(/Suitable Recitals|GDPR Comments|Leave a Reply/i)[0].trim()

  return { title, body }
}

async function main() {
  await mkdir(OUT, { recursive: true })
  for (const f of await readdir(OUT)) {
    if (f.endsWith('.md')) await unlink(join(OUT, f))
  }

  console.log(`Fetching ${TOTAL_ARTICLES} GDPR articles...`)
  let written = 0
  for (let n = 1; n <= TOTAL_ARTICLES; n++) {
    try {
      const { title, body } = await fetchArticle(n)
      if (!body || body.length < 20) {
        console.log(`  art ${n}: empty, skipped`)
        continue
      }
      const num = String(n).padStart(2, '0')
      await writeFile(join(OUT, `art-${num}.md`), `# ${title}\n\n${body}\n`, 'utf8')
      written++
      if (n % 20 === 0) console.log(`  ${n} articles...`)
    } catch (e) {
      console.log(`  art ${n}: ${e.message}`)
    }
    await new Promise((r) => setTimeout(r, 120)) // be gentle with the site
  }

  console.log(`Done: ${written} articles in docs-samples/gdpr/`)
}

main().catch((e) => {
  console.error('ERROR:', e.message)
  process.exit(1)
})
