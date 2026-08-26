#!/usr/bin/env python3
"""Brace-matching census of catch clauses. Classifies each catch body by what it
actually does with the error, not by whether a grep pattern appears in the file."""
import os, re, sys, json, collections

ROOT = "/home/cody/external-rescue/Github/accessible-trade-terminal"
SKIP_DIRS = {"bin", "obj", ".git", "node_modules", "dist", "publish"}

def strip_comments_and_strings(src):
    """Return src with comments blanked (kept same length) and string/char
    literals blanked, so brace matching is not fooled."""
    out = list(src)
    i, n = 0, len(src)
    state = None  # None | 'line' | 'block' | 'str' | 'verb' | 'char' | 'interp'
    while i < n:
        c = src[i]
        nxt = src[i+1] if i+1 < n else ''
        if state is None:
            if c == '/' and nxt == '/':
                state = 'line'; out[i] = out[i+1] = ' '; i += 2; continue
            if c == '/' and nxt == '*':
                state = 'block'; out[i] = out[i+1] = ' '; i += 2; continue
            if c == '@' and nxt == '"':
                state = 'verb'; out[i] = out[i+1] = ' '; i += 2; continue
            if c == '$' and nxt == '"':
                state = 'str'; out[i] = out[i+1] = ' '; i += 2; continue
            if c == '"':
                state = 'str'; out[i] = ' '; i += 1; continue
            if c == "'":
                state = 'char'; out[i] = ' '; i += 1; continue
            i += 1; continue
        if state == 'line':
            if c == '\n': state = None
            else: out[i] = ' '
            i += 1; continue
        if state == 'block':
            if c == '*' and nxt == '/':
                out[i] = out[i+1] = ' '; state = None; i += 2; continue
            if c != '\n': out[i] = ' '
            i += 1; continue
        if state == 'str':
            if c == '\\':
                out[i] = ' '
                if i+1 < n and src[i+1] != '\n': out[i+1] = ' '
                i += 2; continue
            if c == '"': out[i] = ' '; state = None; i += 1; continue
            if c == '\n': state = None; i += 1; continue
            out[i] = ' '; i += 1; continue
        if state == 'verb':
            if c == '"' and nxt == '"':
                out[i] = out[i+1] = ' '; i += 2; continue
            if c == '"': out[i] = ' '; state = None; i += 1; continue
            if c != '\n': out[i] = ' '
            i += 1; continue
        if state == 'char':
            if c == '\\':
                out[i] = ' '
                if i+1 < n: out[i+1] = ' '
                i += 2; continue
            if c == "'": out[i] = ' '; state = None; i += 1; continue
            out[i] = ' '; i += 1; continue
    return ''.join(out)

CATCH_RE = re.compile(r'\bcatch\b')

def find_block(masked, start):
    """start = index of '{'. Return index just past matching '}'."""
    depth = 0
    i = start
    while i < len(masked):
        if masked[i] == '{': depth += 1
        elif masked[i] == '}':
            depth -= 1
            if depth == 0: return i
        i += 1
    return -1

def classify(body_raw, body_masked, exc_type, has_ident):
    """Return (category, flags)."""
    code = body_masked.strip()
    stripped = re.sub(r'\s+', '', code)
    if stripped == '':
        # empty or comment-only
        raw_inner = body_raw.strip()
        if re.sub(r'\s+', '', re.sub(r'//.*|/\*.*?\*/', '', raw_inner, flags=re.S)) == '':
            return ('empty_comment_only' if ('//' in raw_inner or '/*' in raw_inner) else 'empty_bare')
    if re.search(r'\bthrow\b', code):
        return 'rethrow_or_throw'
    return 'handles'

LOG_RE = re.compile(r'\b(_?[Ll]og(ger)?[A-Za-z0-9_]*)\s*[\.\?]|\bLog(Error|Warning|Debug|Trace|Information|Critical)\b|Console\.(Write|Error)|Debug\.WriteLine|Trace\.')
USER_RE = re.compile(r'ReportError|ReportNetworkRetry|Announce|FeedbackRequestEvent|AppErrorEvent|Notify|NotificationHub|Speak|PlayEarcon|ShowError|SurfaceError|ProviderError|StatusMessage|ErrorMessage\s*=|SetError|Toast|Alert\(')

results = []
for dirpath, dirnames, filenames in os.walk(ROOT):
    dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
    for fn in filenames:
        if not (fn.endswith('.cs') or fn.endswith('.razor')):
            continue
        path = os.path.join(dirpath, fn)
        rel = os.path.relpath(path, ROOT)
        try:
            src = open(path, encoding='utf-8-sig').read()
        except Exception:
            continue
        masked = strip_comments_and_strings(src)
        for m in CATCH_RE.finditer(masked):
            # ensure it's a keyword, not part of an identifier
            s = m.start()
            if s > 0 and (masked[s-1].isalnum() or masked[s-1] in '_.'):
                continue
            j = m.end()
            # optional (Type ident) [when (...)]
            exc_type, has_ident, has_when = None, False, False
            k = j
            while k < len(masked) and masked[k] in ' \t\r\n': k += 1
            if k < len(masked) and masked[k] == '(':
                close = find_paren(masked, k) if False else None
                depth, p = 0, k
                while p < len(masked):
                    if masked[p] == '(': depth += 1
                    elif masked[p] == ')':
                        depth -= 1
                        if depth == 0: break
                    p += 1
                decl = src[k+1:p].strip()
                parts = decl.split()
                exc_type = parts[0] if parts else '?'
                has_ident = len(parts) > 1
                k = p + 1
            else:
                exc_type = '(bare)'
            while k < len(masked) and masked[k] in ' \t\r\n': k += 1
            if masked[k:k+4] == 'when':
                has_when = True
                depth, p = 0, masked.index('(', k)
                while p < len(masked):
                    if masked[p] == '(': depth += 1
                    elif masked[p] == ')':
                        depth -= 1
                        if depth == 0: break
                    p += 1
                k = p + 1
                while k < len(masked) and masked[k] in ' \t\r\n': k += 1
            if k >= len(masked) or masked[k] != '{':
                continue
            end = find_block(masked, k)
            if end < 0: continue
            body_raw = src[k+1:end]
            body_masked = masked[k+1:end]
            cat = classify(body_raw, body_masked, exc_type, has_ident)
            line = src.count('\n', 0, s) + 1
            results.append({
                'file': rel, 'line': line, 'type': exc_type, 'when': has_when,
                'cat': cat,
                'logs': bool(LOG_RE.search(body_raw)),
                'user': bool(USER_RE.search(body_raw)),
                'body': body_raw.strip()[:400],
            })

json.dump(results, open(sys.argv[1] if len(sys.argv) > 1 else '/dev/stdout', 'w'), indent=1)
print(f"total catch clauses: {len(results)}", file=sys.stderr)
