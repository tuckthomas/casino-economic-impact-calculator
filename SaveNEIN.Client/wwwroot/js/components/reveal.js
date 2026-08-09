window.initScrollReveal = function () {
    if (window.__saveNeinRevealObservers) {
        window.__saveNeinRevealObservers.forEach(observer => observer.disconnect());
    }

    const prefersReducedMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const allReveals = Array.from(document.querySelectorAll('.scroll-reveal'));

    if (prefersReducedMotion) {
        allReveals.forEach(el => el.classList.remove('opacity-0', 'translate-y-8'));
        window.__saveNeinRevealObservers = [];
        return;
    }

    const repeatingReveals = allReveals.filter(el => el.closest('.tribal-impact-banner'));
    const oneTimeReveals = allReveals.filter(el => !el.closest('.tribal-impact-banner'));

    const repeatingContainers = new Set(
        repeatingReveals
            .map(el => el.closest('h2') || el.parentElement)
            .filter(Boolean)
    );

    const repeatObserver = new IntersectionObserver((entries) => {
        entries.forEach(entry => {
            const reveals = entry.target.querySelectorAll('.scroll-reveal');
            reveals.forEach(el => {
                if (entry.isIntersecting) {
                    el.classList.remove('opacity-0', 'translate-y-8');
                } else {
                    el.classList.add('opacity-0', 'translate-y-8');
                }
            });
        });
    }, { threshold: 0.3 });

    repeatingContainers.forEach(container => repeatObserver.observe(container));

    const oneTimeObserver = new IntersectionObserver((entries) => {
        entries.forEach(entry => {
            if (!entry.isIntersecting) return;

            const reveals = entry.target.querySelectorAll('.scroll-reveal');
            reveals.forEach(el => {
                if (!el.closest('.tribal-impact-banner')) {
                    el.classList.remove('opacity-0', 'translate-y-8');
                }
            });
            oneTimeObserver.unobserve(entry.target);
        });
    }, { threshold: 0.3 });

    const oneTimeContainers = new Set(
        oneTimeReveals
            .map(el => el.closest('h2') || el.parentElement)
            .filter(Boolean)
    );

    oneTimeContainers.forEach(container => oneTimeObserver.observe(container));
    window.__saveNeinRevealObservers = [repeatObserver, oneTimeObserver];
};