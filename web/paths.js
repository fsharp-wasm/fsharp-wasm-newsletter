// Where the page finds the JavaScript glue (lib/runtime) and the compiled module in a checkout.
// `bash build/site.sh` writes the paths of the deployed site (everything beside index.html) into dist/site/paths.js.
export const runtime = "../lib/runtime/runtime/";
export const wasm = "../dist/NewsletterDesigner.wasm";
