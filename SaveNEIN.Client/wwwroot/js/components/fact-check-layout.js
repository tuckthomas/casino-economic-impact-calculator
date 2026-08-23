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
        if (layout.querySelector('.fact-check-entry__details[open]')) return;

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

        cards.forEach(card => {
            const main = card.querySelector('.fact-check-entry__card-main');
            if (!main) return;

            card.style.setProperty(
                '--fact-check-collapsed-main-height',
                `${Math.ceil(main.getBoundingClientRect().height)}px`);
        });
    }

    function scheduleAlignment() {
        window.cancelAnimationFrame(resizeFrame);
        resizeFrame = window.requestAnimationFrame(alignPairs);
    }

    function releaseExpandedDetails(details) {
        const activeCard = details.closest('.fact-check-entry__card');
        if (!activeCard) return;

        document.querySelectorAll('.fact-check-entry__card').forEach(card => {
            if (card === activeCard) return;

            card.style.minHeight = '';
        });
    }

    function init() {
        scheduleAlignment();

        document.querySelectorAll('.fact-check-entry__details').forEach(details => {
            if (details.dataset.layoutToggleBound === 'true') return;

            details.dataset.layoutToggleBound = 'true';
            details.addEventListener('toggle', () => {
                if (details.open) {
                    releaseExpandedDetails(details);
                    return;
                }

                // Reapply bottom alignment only after the open card is closed.
                alignPairs();
            });
        });

        if (document.fonts?.ready) {
            document.fonts.ready.then(scheduleAlignment);
        }

        if (!initialized) {
            window.addEventListener('resize', scheduleAlignment, { passive: true });
            initialized = true;
        }
    }

    function scrollToClaim(slug) {
        const encodedSlug = window.CSS?.escape ? window.CSS.escape(slug) : slug;
        const suffix = window.matchMedia('(max-width: 767px)').matches
            ? '-all-fact-checks-mobile'
            : '-all-fact-checks-desktop-';
        const selector = window.matchMedia('(max-width: 767px)').matches
            ? `#fact-check-${encodedSlug}${suffix}`
            : `[id^="fact-check-${encodedSlug}${suffix}"]`;
        const claim = document.querySelector(selector);

        if (claim) {
            requestAnimationFrame(() => claim.scrollIntoView({ block: 'start' }));
        }
    }

    return { init, scrollToClaim };
})();
