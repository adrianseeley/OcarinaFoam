// Create parent directories and write UTF-8 text inside the generated case tree.
// The caller owns template substitution and destructive regeneration; no merge with a
// previous case or preservation of local generated-file edits is attempted here.

const fs = require('fs');
const path = require('path');

module.exports = function writeCaseFile(caseDirectory, relativePath, content) {
    const target = path.join(caseDirectory, relativePath);
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.writeFileSync(target, content, 'utf8');
};