// node inline-juice.mjs <foundation-emails.css> <raw.html> <out.html> — what Foundation's own build does: juice, with its compiled CSS.
import juice from "juice";
import { readFileSync, writeFileSync } from "node:fs";
const [css, html, out] = process.argv.slice(2);
writeFileSync(out, juice("<style>" + readFileSync(css, "utf8") + "</style>" + readFileSync(html, "utf8"),
  { removeStyleTags: true, preserveMediaQueries: true, applyWidthAttributes: false, applyHeightAttributes: false, applyAttributesTableElements: false }));
