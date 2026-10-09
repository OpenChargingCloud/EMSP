/** Hand the browser a file to save: a contract, an account key, a ticket. */
export function download(content: Uint8Array | string, filename: string, type: string): void {

    const blob = new Blob([content as BlobPart], { type });
    const url  = URL.createObjectURL(blob);
    const link = document.createElement('a');

    link.href     = url;
    link.download = filename;

    document.body.appendChild(link);
    link.click();
    link.remove();

    // Not at once: some browsers start the download after the click returns.
    setTimeout(() => URL.revokeObjectURL(url), 10_000);

}
