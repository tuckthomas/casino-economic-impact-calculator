window.initScrollReveal = function () {
    const observer = new IntersectionObserver((entries) => {
        entries.forEach(entry => {
            if (entry.isIntersecting) {
                const reveals = entry.target.querySelectorAll('.scroll-reveal');
                reveals.forEach(el => {
                    el.classList.remove('opacity-0', 'translate-y-8');
                });
                observer.unobserve(entry.target);
            }
        });
    }, { threshold: 0.3 });

    const targets = document.querySelectorAll('.scroll-reveal');
    targets.forEach(t => {
        const container = t.closest('h2') || t.parentElement;
        if (container) observer.observe(container);
    });
};