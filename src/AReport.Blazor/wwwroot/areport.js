// Browser helpers for AReport.Blazor. Loaded on demand as an ES module.

export async function createObjectUrl(streamReference, contentType) {
    const buffer = await streamReference.arrayBuffer();
    return URL.createObjectURL(new Blob([buffer], { type: contentType }));
}

export function revokeObjectUrl(url) {
    URL.revokeObjectURL(url);
}

export async function download(fileName, contentType, streamReference) {
    const url = await createObjectUrl(streamReference, contentType);
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 10000);
}
