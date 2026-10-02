// EC-006-1 — Recipient parsing for the "send link" flow.

export const MAX_EMAILS = 20; // EC-006-1 — max recipients per send

const EMAIL_REGEX = /^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$/;

/** Parse raw textarea input into normalized unique email addresses. */
export function parseRecipients(raw: string): { valid: string[]; invalid: string[] } {
  const tokens = raw.split(/[\r\n,;\s]+/).map(t => t.trim().toLowerCase()).filter(Boolean);
  const seen = new Set<string>();
  const valid: string[] = [];
  const invalid: string[] = [];

  for (const token of tokens) {
    if (seen.has(token)) continue; // duplicate — skip silently
    seen.add(token);
    if (EMAIL_REGEX.test(token)) {
      valid.push(token);
    } else {
      invalid.push(token);
    }
  }
  return { valid, invalid };
}
