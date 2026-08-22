window.FactCheckLayout = (() => {
    let initialized = false;
    let resizeFrame = 0;

    function alignPairs() {
        const layout = document.querySelector('.fact-check-desktop-columns');
        if (!layout) return;

        const columns = layout.querySelectorAll(':scope > .fact-check-column');
        const cards = layout.querySelectorAll('.fact-check-entry__card');

        cards.forEach(card => card.style.minHeight = '');

        if (window.matchMedia('(max-width: 767px)').matches || columns.length !== 2) return;

        const leftCards = columns[0].querySelectorAll(':scope > .fact-check-entry > .fact-check-entry__card');
        const rightCards = columns[1].querySelectorAll(':scope > .fact-check-entry > .fact-check-entry__card');
        const pairCount = Math.min(leftCards.length, rightCards.length);

        // Hide expanded detail bodies only while measuring the collapsed card heights.
        // This class is added and removed in the same frame, so there is no visible flash.
        layout.classList.add('is-measuring');

        for (let index = 0; index < pairCount; index++) {
            const pairHeight = Math.ceil(Math.max(
                leftCards[index].getBoundingClientRect().height,
                rightCards[index].getBoundingClientRect().height));

            leftCards[index].style.minHeight = `${pairHeight}px`;
            rightCards[index].style.minHeight = `${pairHeight}px`;
        }

        layout.classList.remove('is-measuring');
    }

    function scheduleAlignment() {
        window.cancelAnimationFrame(resizeFrame);
        resizeFrame = window.requestAnimationFrame(alignPairs);
    }

    function init() {
        scheduleAlignment();

        if (document.fonts?.ready) {
            document.fonts.ready.then(scheduleAlignment);
        }

        if (!initialized) {
            window.addEventListener('resize', scheduleAlignment, { passive: true });
            initialized = true;
        }
    }

    return { init };
})();
