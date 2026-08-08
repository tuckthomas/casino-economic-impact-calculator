window.TOC = (function ()
{
    function setActive(targetId)
    {
        const tocLinks = document.querySelectorAll('.toc-link');
        tocLinks.forEach(link =>
        {
            link.classList.remove('active', 'opacity-100');
            link.classList.add('opacity-60');
        });

        const activeLink = document.querySelector(`.toc-link[data-target="${targetId}"]`);
        if (activeLink)
        {
            activeLink.classList.add('active', 'opacity-100');
            activeLink.classList.remove('opacity-60');
        }
    }

    function init()
    {
        const tocLinks = document.querySelectorAll('.toc-link');

        // Get all target IDs from the TOC links
        const targetIds = Array.from(tocLinks).map(link => link.dataset.target).filter(Boolean);

        // Select all sections and any element with an ID that matches a TOC target
        const sections = [];
        targetIds.forEach(id =>
        {
            const el = document.getElementById(id);
            if (el) sections.push(el);
        });

        // Add click handlers to immediately update active state
        tocLinks.forEach(link =>
        {
            link.addEventListener('click', function ()
            {
                const target = this.dataset.target;
                if (target)
                {
                    setActive(target);
                    // Also update after scroll animation completes
                    setTimeout(() => setActive(target), 500);
                }
            });
        });

        const observerOptions = {
            root: null,
            rootMargin: '-45% 0px -45% 0px',
            threshold: 0
        };

        const observer = new IntersectionObserver((entries) =>
        {
            entries.forEach(entry =>
            {
                if (entry.isIntersecting)
                {
                    setActive(entry.target.id);
                }
            });
        }, observerOptions);

        sections.forEach(section =>
        {
            observer.observe(section);
        });
    }

    return { init: init };
})();
