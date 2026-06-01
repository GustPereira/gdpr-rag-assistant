import type { Citation } from '../lib/api'

// Renders a cited LGPD article: its number (header) and full text, plus the
// similarity score from the search.
export default function ArticleCard({ citation }: { citation: Citation }) {
  const pct = Math.round(citation.score * 100)

  return (
    <div className="rounded-xl border border-slate-700 bg-slate-900 p-4">
      <div className="flex items-center justify-between gap-2">
        <h3 className="text-sm font-semibold text-emerald-400">
          [{citation.ref}] {citation.title}
        </h3>
        <span className="shrink-0 rounded bg-slate-700 px-1.5 py-0.5 text-xs text-slate-300">
          {pct}%
        </span>
      </div>
      <p className="mt-2 max-h-56 overflow-y-auto whitespace-pre-wrap text-xs leading-relaxed text-slate-300">
        {citation.text}
      </p>
    </div>
  )
}
