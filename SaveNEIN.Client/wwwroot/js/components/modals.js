window.Modals = (function ()
{
    /* --- SOURCES & WORD DETAIL MODAL --- */
    function initSourcesModal()
    {
        const WORD_DATA = {
            "LOW WAGE JOBS": {
                desc: "Data regarding Rising Star Casino shows essential service roles pay significantly less than market rate for fast food workers in Fort Wayne. <br><br><strong>The Wage Gap:</strong><br>• Line Cook: $13.25-$17.75/hr (Casino) vs $20.27/hr (FW Fast Food Avg).<br>You could earn ~35% more at a local Wendy's or Chick-fil-A.",
                url: "https://www.ziprecruiter.com/Salaries/Fast-Food-Salary-in-Fort-Wayne,IN"
            },
            "ADDICTION": { desc: "Research indicates that 96% of individuals with gambling disorder have a co-occurring psychiatric disorder. The APA defines it as leading to clinically significant impairment.", url: "https://www.addiction.rutgers.edu/about-addiction/facts-and-figures/gambling/" },
            "HUMAN TRAFFICKING": { desc: "The AGA admits casinos are used by traffickers to 'facilitate criminal activity.' Survivors report traffickers use casino anonymity to mask victim exploitation.", url: "https://www.americangaming.org/wp-content/uploads/2022/06/AGA-Preventing-and-Combating-Human-Trafficking-in-the-Gaming-Industry.pdf" },
            "EMBEZZLEMENT": { desc: "A DOJ study found pathological gamblers were 3 to 5 times more likely to be arrested, often committing robbery or theft to finance addiction.", url: "https://www.ojp.gov/pdffiles1/nij/203197.pdf" },
            "POVERTY": { desc: "Despite revenue, poverty rates for Native American communities with casinos remain higher than national average (19.6% vs 12.1%).", url: "https://www.actionnetwork.com/news/new-research-highlights-economic-changes-driven-by-tribal-casinos" },
            "CHILD NEGLECT": { desc: "Systematic reviews found significant association between problem gambling and multiple forms of child maltreatment, including physical abuse.", url: "https://aifs.gov.au/resources/short-articles/child-maltreatment-and-problem-gambling" },
            "DOMESTIC VIOLENCE": { desc: "Women whose partners have a gambling problem are 10.5 times more likely to experience intimate partner violence.", url: "https://dbhdd.georgia.gov/document/document/trauma0911pdf/download" },
            "BANKRUPTCIES": { desc: "Counties with casinos experience a 2.3% higher annual growth rate in personal bankruptcies compared to non-casino counties.", url: "https://researchworks.creighton.edu/esploro/outputs/other/The-impact-of-casino-gambling-on/991005931167902656" },
            "DIVORCES": { desc: "The NGISC reports a lifetime divorce rate of 39.5% for problem gamblers, compared to 18.2% for the general population.", url: "https://www.mccaberussell.com/blog/divorcing-a-gambling-addict-what-you-should-know/" },
            "SUBSTANCE ABUSE": { desc: "Pathological gamblers have a 5.5 times higher risk for substance use disorders compared to non-gamblers.", url: "https://www.tandfonline.com/doi/full/10.1080/19585969.2025.2484288" },
            "HOMELESSNESS": { desc: "A University of Cambridge study found homeless individuals are 10 times more likely to be problem gamblers.", url: "https://www.stoppredatorygambling.org/study-examines-the-link-between-homelessness-and-problem-gambling/" },
            "FORECLOSURE": { desc: "The sharp rise in personal bankruptcies and 'bad debt' in casino counties directly correlates to housing instability.", url: "https://k-12talk.com/gambling-debt-understanding-the-average-contributing-factors-and-recovery/" },
            "CORRUPTION": { desc: "Empirical models indicate 'Granger causality' where casino revenues can 'cause' political corruption via rent-seeking behavior.", url: "https://www.researchgate.net/publication/267883543_The_Casino_Industry_and_the_Corruption_of_US_Public_Officials" },
            "BAD DEBT": { desc: "Gambling debt is hidden due to shame. 'Affected others' report severe stress, ill health, and relationship breakdown.", url: "https://www.researchgate.net/publication/305495934_Breaking_Bad_Comparing_Gambling_Harms_Among_Gamblers_and_Affected_Others" },
            "FATAL ACCIDENTS": { desc: "Alcohol-related fatal traffic accidents increase by approx 9.2% in counties that open casinos.", url: "https://www.stoppredatorygambling.org/wp-content/uploads/2012/12/Journal-of-Health-Economics-Impact-of-Casinos-on-Fatal-Alcohol-related-Traffic-Accidents.pdf" },
            "LOCAL BUSINESSES LOST": { desc: "The Fed Reserve identifies a 'substitution effect' where consumers divert spending from local restaurants to the casino.", url: "https://www.stlouisfed.org/community-development/publications/bridges/casinos-and-economic-development-a-look-at-the-issues" },
            "MENTAL HEALTH ISSUES": { desc: "Gambling disorder has the highest suicide attempt rate of any addiction (up to 23.2%).", url: "https://www.oapgg.org/newsletters-and-updates/blog-post-title-three-9faw2" }
        };

        const BAIT_WORDS = [
            "WORLD-CLASS DESTINATION", "INCREDIBLE OPPORTUNITY", "ECONOMIC ENGINE",
            "REVITALIZED INFRASTRUCTURE", "NEIGHBORHOOD IMPROVEMENTS", "HUMANITARIAN FUND",
            "RESPONSIBLE GAMING", "RETAIN TALENT", "COMMUNITY WELL-BEING", "PREMIER DESTINATION", "GOOD PAYING JOBS"
        ];

        let currentWordIndex = 0;
        const TARGET_KEYS = Object.keys(WORD_DATA);

        window.openSourcesModal = function ()
        {
            const m = document.getElementById('sources-modal');
            const baitList = document.getElementById('bait-list');
            const hookList = document.getElementById('hook-list');
            if (!m || !baitList || !hookList) return;

            baitList.innerHTML = BAIT_WORDS.map(word => `
                <li class="flex items-center gap-3 bg-emerald-50 dark:bg-emerald-900/20 p-3 rounded-xl border border-emerald-100 dark:border-emerald-800">
                    <span class="font-semibold text-emerald-900 dark:text-emerald-100 text-sm">${word}</span>
                </li>
            `).join('');

            hookList.innerHTML = TARGET_KEYS.map(word => `
                <li class="flex flex-wrap items-center justify-between gap-3 bg-red-50 dark:bg-red-900/20 p-3 rounded-xl border border-red-100 dark:border-red-800">
                    <span class="font-semibold text-red-900 dark:text-red-100 text-sm">${word}</span>
                    <button onclick="openWordModal('${word}')" class="px-3 py-1 bg-white border border-red-200 text-red-600 text-sm font-bold rounded-lg uppercase">Learn More</button>
                </li>
            `).join('');

            m.classList.remove('opacity-0', 'pointer-events-none');
            document.body.classList.add('overflow-hidden');
        };

        window.closeSourcesModal = function ()
        {
            const m = document.getElementById('sources-modal');
            if (m)
            {
                m.classList.add('opacity-0', 'pointer-events-none');
                document.body.classList.remove('overflow-hidden');
            }
        };

        window.openWordModal = function (word)
        {
            currentWordIndex = TARGET_KEYS.indexOf(word);
            updateWordModalContent();
            const wm = document.getElementById('word-detail-modal');
            const content = document.getElementById('word-modal-content');
            wm.classList.remove('opacity-0', 'pointer-events-none');
            setTimeout(() => content.classList.remove('scale-95'), 10);
        };

        function updateWordModalContent()
        {
            const word = TARGET_KEYS[currentWordIndex];
            const data = WORD_DATA[word];
            document.getElementById('word-modal-title').textContent = word;
            document.getElementById('word-modal-desc').innerHTML = data.desc;
            document.getElementById('word-modal-link').href = data.url;
        }

        window.nextWord = function () { currentWordIndex = (currentWordIndex + 1) % TARGET_KEYS.length; updateWordModalContent(); };
        window.prevWord = function () { currentWordIndex = (currentWordIndex - 1 + TARGET_KEYS.length) % TARGET_KEYS.length; updateWordModalContent(); };
        window.closeWordModal = function ()
        {
            const wm = document.getElementById('word-detail-modal');
            wm.classList.add('opacity-0', 'pointer-events-none');
        };
    }

    /* --- FLIPBOOK MODAL --- */
    // Flipbook is now handled by flipbook.js which uses PDF.js + StPageFlip
    function initFlipbook()
    {
        // No-op - flipbook.js handles this
    }

    /* --- METHODOLOGY MODAL --- */
    function initMethodologyModal()
    {
        window.openMethodologyModal = function ()
        {
            const m = document.getElementById('methodology-modal');
            if (m)
            {
                m.classList.remove('opacity-0', 'pointer-events-none');
                document.body.classList.add('overflow-hidden');
            }
        };
        window.closeMethodologyModal = function ()
        {
            const m = document.getElementById('methodology-modal');
            if (m)
            {
                m.classList.add('opacity-0', 'pointer-events-none');
                document.body.classList.remove('overflow-hidden');
            }
        };
    }

    return {
        init: function ()
        {
            initMethodologyModal();
            initSourcesModal();
            initFlipbook();
        }
    };
})();
