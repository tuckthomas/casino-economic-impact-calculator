window.AppButtonDepth = (() => {
    const buttonSelector = '.btn-raised';

    function parseRgb(value) {
        const match = value.match(/^rgba?\(([^)]+)\)$/i);
        if (!match) return null;

        const parts = match[1]
            .split(/[\s,/]+/)
            .filter(Boolean)
            .map(Number);

        if (parts.length < 3 || parts.some(Number.isNaN) || (parts.length > 3 && parts[3] === 0)) {
            return null;
        }

        return parts.slice(0, 3);
    }

    function applyDepth(button) {
        const color = parseRgb(window.getComputedStyle(button).backgroundColor);
        if (!color) return;

        const depth = color.map(channel => Math.round(channel * 0.62));
        const ambient = color.map(channel => Math.round(channel * 0.42));

        button.style.setProperty('--btn-raised-depth', `rgb(${depth.join(' ')})`);
        button.style.setProperty('--btn-raised-ambient', `rgb(${ambient.join(' ')} / 0.34)`);
    }

    function refresh(root = document) {
        root.querySelectorAll?.(buttonSelector).forEach(applyDepth);
        if (root.matches?.(buttonSelector)) applyDepth(root);
    }

    function init() {
        refresh();

        new MutationObserver(records => {
            for (const record of records) {
                record.addedNodes.forEach(node => {
                    if (node.nodeType === Node.ELEMENT_NODE) refresh(node);
                });
            }
        }).observe(document.body, { childList: true, subtree: true });

        document.addEventListener('pointerover', event => {
            const button = event.target.closest?.(buttonSelector);
            if (button) requestAnimationFrame(() => applyDepth(button));
        }, true);

        document.addEventListener('focusin', event => {
            const button = event.target.closest?.(buttonSelector);
            if (button) requestAnimationFrame(() => applyDepth(button));
        });
    }

    return { init, refresh };
})();

window.AppButtonDepth.init();
