// Replace uppercase template tokens from a supplied value map; reject unknown tokens.
// This is literal text substitution, not a template language or escaping layer. Paths,
// colours and other inserted strings must already be valid in their destination syntax.
// Tokens in comments are substituted too, so documentation uses plain parameter names.

module.exports = function hydrate(template, values) {
    return template.replace(/\{\{([A-Z_]+)\}\}/g, (match, key) => {
        if (!Object.hasOwn(values, key)) {
            throw new Error(`No value provided for template token ${match}`);
        }
        return String(values[key]);
    });
};