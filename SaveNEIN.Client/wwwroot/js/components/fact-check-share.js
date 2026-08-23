window.FactCheckShare = window.FactCheckShare || {
    async copyImage(imageUrl) {
        if (!navigator.clipboard?.write || typeof ClipboardItem === "undefined") {
            throw new Error("Image clipboard access is unavailable in this browser.");
        }

        const response = await fetch(imageUrl, { cache: "no-store" });
        if (!response.ok) {
            throw new Error("The fact-check image could not be loaded.");
        }

        const image = await response.blob();
        await navigator.clipboard.write([
            new ClipboardItem({ "image/png": image })
        ]);
    }
};
