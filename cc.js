#!/usr/bin/env node
// Source-bundling utility for review/context exchange; it is not part of the simulation.
// node cc.js writes cc.txt with relative filename delimiters around each readable text file.
// The binary filter is an extension list plus a 512-byte NUL heuristic, not a full detector.
// It does not obey .gitignore: generated cases and large plaintext outputs can be included.
// Review the bundle before sharing; the printed count is candidate files, not text files.

// Bundles every text file in the repo (excluding .git and binary files) into a single cc.txt file.

const fs = require("fs");
const path = require("path");

const rootDir = __dirname;
const outputFile = path.join(rootDir, "cc.txt");

const ignoredDirs = [".git", "node_modules"];
const ignoredExtensions = [
	".ttf", ".otf", ".woff", ".woff2", ".eot",
	".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp",
	".pdf", ".zip", ".tar", ".gz", ".7z", ".rar",
	".exe", ".dll", ".so", ".dylib", ".bin",
	".mp3", ".mp4", ".mov", ".avi", ".wav",
];

function isBinaryFile(filePath) {
	const ext = path.extname(filePath).toLowerCase();
	if (ignoredExtensions.indexOf(ext) !== -1) {
		return true;
	}

	const buffer = Buffer.alloc(512);
	let bytesRead = 0;
	let fd;
	try {
		fd = fs.openSync(filePath, "r");
		bytesRead = fs.readSync(fd, buffer, 0, buffer.length, 0);
	} catch (err) {
		return true;
	} finally {
		if (fd !== undefined) {
			fs.closeSync(fd);
		}
	}

	for (let i = 0; i < bytesRead; i++) {
		if (buffer[i] === 0) {
			return true;
		}
	}
	return false;
}

function collectFiles(dir, files) {
	const entries = fs.readdirSync(dir, { withFileTypes: true });
	for (let i = 0; i < entries.length; i++) {
		const entry = entries[i];
		const fullPath = path.join(dir, entry.name);

		if (entry.isDirectory()) {
			if (ignoredDirs.indexOf(entry.name) !== -1) {
				continue;
			}
			collectFiles(fullPath, files);
		} else if (entry.isFile()) {
			if (fullPath === outputFile) {
				continue;
			}
			files.push(fullPath);
		}
	}
}

function main() {
	const files = [];
	collectFiles(rootDir, files);
	files.sort();

	const chunks = [];
	for (let i = 0; i < files.length; i++) {
		const filePath = files[i];
		if (isBinaryFile(filePath)) {
			continue;
		}

		const relativePath = path.relative(rootDir, filePath).split(path.sep).join("/");
		const contents = fs.readFileSync(filePath, "utf8");

		chunks.push("<" + relativePath + ">\n");
		chunks.push(contents);
		if (!contents.endsWith("\n")) {
			chunks.push("\n");
		}
		chunks.push("</" + relativePath + ">\n\n");
	}

	fs.writeFileSync(outputFile, chunks.join(""), "utf8");
	console.log("Wrote " + files.length + " candidate files (bundled) to " + outputFile);
}

main();
