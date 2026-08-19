import type { ReactElement } from 'react';
import {
  ArrowRight,
  Brain,
  CheckCircle,
  Clock,
  Code,
  Database,
  FolderGit,
  GitBranch,
  Layers,
  Package,
  PenTool,
  Play,
  Plug,
  Scale,
  Send,
  ShieldCheck,
  Split,
  Square,
  Table,
  Terminal,
  XCircle,
} from 'lucide-react';

// ═══════════════════════════════════════════════════════════
// Step icon resolver
//
// Step schemas declare `icon` as a lucide icon name (kebab-case,
// e.g. 'database', 'git-branch'). This maps those names to the
// matching lucide-react components so callers never render the raw
// string — which would overflow the fixed-size icon box and
// collide with the node title.
// ═══════════════════════════════════════════════════════════

const STEP_ICONS: Record<string, React.ElementType> = {
  'arrow-right': ArrowRight,
  brain: Brain,
  'check-circle': CheckCircle,
  clock: Clock,
  code: Code,
  database: Database,
  'folder-git': FolderGit,
  'git-branch': GitBranch,
  layers: Layers,
  'pen-tool': PenTool,
  play: Play,
  plug: Plug,
  scale: Scale,
  send: Send,
  'shield-check': ShieldCheck,
  'split-merge': Split,
  stop: Square,
  table: Table,
  terminal: Terminal,
  'x-circle': XCircle,
};

/**
 * Resolve a step schema icon name to a sized lucide icon.
 * Unknown or missing names fall back to the generic package icon
 * so a bad value can never spill out of its container.
 */
export function stepIcon(name: string | undefined | null, className = 'w-4 h-4'): ReactElement {
  const Cmp = name ? STEP_ICONS[name] : undefined;
  if (Cmp) return <Cmp className={className} />;
  return <Package className={className} />;
}
