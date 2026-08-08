/**
 * Reality Modal Carousel
 * Opens a modal with left/right navigation for cycling through reality items
 */
(function ()
{
    'use strict';

    // Reality items data by category
    const REALITY_DATA = {
        'economic-engine': [
            {
                title: 'Local Businesses Lost',
                content: 'The primary economic argument for the proposed Northeast Indiana casino is that it will generate "new" money. This assumes it functions as a tourist destination. However, the Spectrum Study (2025) confirms that nearly 95% of visitors live within 60–90 minutes, defining it as a "convenience" venue. This triggers the <strong>Substitution Effect</strong>: money is not imported; it is simply diverted from local restaurants, theaters, and retail to the casino, creating a net loss for the existing economy.',
                source: 'https://www.stlouisfed.org/community-development/publications/bridges/casinos-and-economic-development-a-look-at-the-issues'
            },
            {
                title: 'Bankruptcies',
                content: 'Counties that legalize casino gambling experience a 2.3% higher annual growth rate in personal bankruptcies compared to non-casino counties. Bankruptcy rates eventually exceed those of non-casino counties after nine years of operations.',
                source: 'https://researchworks.creighton.edu/esploro/outputs/other/The-impact-of-casino-gambling-on/991005931167902656'
            },
            {
                title: 'Poverty',
                content: 'Despite casinos generating revenue, poverty rates for Native American communities with casinos remain higher than the national average (19.6% vs 12.1%). The "economic engine" argument often fails to account for the lack of wealth distribution to the poorest residents.',
                source: 'https://www.actionnetwork.com/news/new-research-highlights-economic-changes-driven-by-tribal-casinos'
            },
            {
                title: 'Foreclosure',
                content: 'While foreclosure specific data varies, the sharp rise in personal bankruptcies and "bad debt" in casino counties directly correlates to housing instability and loss of assets.',
                source: 'https://k-12talk.com/gambling-debt-understanding-the-average-contributing-factors-and-recovery/'
            },
            {
                title: 'Bad Debt',
                content: 'Gambling debt is often hidden due to shame, but studies show significant financial harm to families. "Affected others" (family/friends) report severe stress, ill health, and relationship breakdown due to the gambler\'s debt.',
                source: 'https://www.researchgate.net/publication/305495934_Breaking_Bad_Comparing_Gambling_Harms_Among_Gamblers_and_Affected_Others'
            }
        ],
        'jobs-promise': [
            {
                title: 'The "5,520 Jobs" Myth',
                content: 'The "5,520 Jobs" headline is a deception. They are mostly <strong>temporary construction jobs</strong>. Once the concrete dries, those workers leave, and the taxpayers are left with the 30-year social bill. In a tight labor market, the 1,024 projected permanent jobs gains will likely be negated by job losses from local small businesses (restaurants, bars, etc.) that are cannibalized by the casino.',
                source: null
            },
            {
                title: 'Low Wage Jobs',
                content: 'The data specifically regarding Full House Resorts\' \'Rising Star Casino\' shows that essential service roles pay significantly less than the current market rate for fast food workers in Fort Wayne. Line Cooks at the casino earn $13.25-$17.75/hr vs $20.27/hr at local fast food chains.',
                source: 'https://www.ziprecruiter.com/Salaries/Fast-Food-Salary-in-Fort-Wayne,IN'
            },
            {
                title: 'Embezzlement',
                content: 'A Dept. of Justice study found that arrestees identified as pathological gamblers were 3 to 5 times more likely to be arrested than the general population, often committing robbery, assault, or theft to finance their addiction.',
                source: 'https://www.ojp.gov/pdffiles1/nij/203197.pdf'
            }
        ],
        'community-health': [
            {
                title: 'Addiction',
                content: 'Research indicates that 96% of individuals with gambling disorder have a co-occurring psychiatric disorder. The American Psychiatric Association defines it as a disorder leading to clinically significant impairment, where sufferers exhaust savings and damage family relationships.',
                source: 'https://www.addiction.rutgers.edu/about-addiction/facts-and-figures/gambling/'
            },
            {
                title: 'Homelessness',
                content: 'A University of Cambridge study found that homeless individuals are 10 times more likely to be problem gamblers than the non-homeless population, establishing a "bi-directional" link where gambling leads to homelessness and vice versa.',
                source: 'https://www.stoppredatorygambling.org/study-examines-the-link-between-homelessness-and-problem-gambling/'
            },
            {
                title: 'Mental Health Issues',
                content: 'Gambling disorder has the highest suicide attempt rate of any addiction. In one state study, 23.2% of those meeting criteria for gambling disorder had attempted suicide.',
                source: 'https://www.oapgg.org/newsletters-and-updates/blog-post-title-three-9faw2'
            },
            {
                title: 'Substance Abuse',
                content: 'There is a high comorbidity between gambling and substance abuse. Pathological gamblers have a 5.5 times higher risk for substance use disorders compared to non-gamblers, often using drugs or alcohol to cope with losses.',
                source: 'https://www.tandfonline.com/doi/full/10.1080/19585969.2025.2484288'
            },
            {
                title: 'Fatal Accidents',
                content: 'A study in the Journal of Health Economics estimates that alcohol-related fatal traffic accidents increase by approximately 9.2% in counties that open casinos.',
                source: 'https://www.stoppredatorygambling.org/wp-content/uploads/2012/12/Journal-of-Health-Economics-Impact-of-Casinos-on-Fatal-Alcohol-related-Traffic-Accidents.pdf'
            }
        ],
        'world-class': [
            {
                title: 'The Saturation Barrier',
                content: 'The Spectrum Study (2025) projects that nearly 95% of visitors will come from within a 60–90 minute "catchment area," mathematically defining this as a "convenience casino" rather than a tourist destination. Fort Wayne is surrounded by established competitors: South Bend (Four Winds), Toledo (Hollywood), Battle Creek (FireKeepers), and Shelbyville (Horseshoe). Without a unique global hook, a standard regional casino cannot "pull" tourists past the venues they already drive past.',
                source: null
            },
            {
                title: 'Human Trafficking',
                content: 'The American Gaming Association admits that casinos are used by traffickers to "facilitate criminal activity" and "seek out potential buyers." Survivors report that traffickers use the anonymity of casino environments to mask the movement and exploitation of victims.',
                source: 'https://www.americangaming.org/wp-content/uploads/2022/06/AGA-Preventing-and-Combating-Human-Trafficking-in-the-Gaming-Industry.pdf'
            },
            {
                title: 'Corruption',
                content: 'Empirical models indicate a "Granger causality" where casino revenues can "cause" political corruption. The large cash flows attract rent-seeking behavior from politicians and can lead to the erosion of public sector integrity.',
                source: 'https://www.researchgate.net/publication/267883543_The_Casino_Industry_and_the_Corruption_of_US_Public_Officials'
            },
            {
                title: 'Domestic Violence',
                content: 'Women whose partners have a gambling problem are 10.5 times more likely to experience intimate partner violence. Nearly 25% of pathological gamblers report engaging in spousal abuse.',
                source: 'https://dbhdd.georgia.gov/document/document/trauma0911pdf/download'
            },
            {
                title: 'Child Neglect',
                content: 'A systematic review found a significant association between problem gambling and multiple forms of child maltreatment, including physical abuse and neglect. The financial strain and time consumption of gambling directly erode parental capacity.',
                source: 'https://aifs.gov.au/resources/short-articles/child-maltreatment-and-problem-gambling'
            },
            {
                title: 'Divorces',
                content: 'The National Gambling Impact Study Commission reports a lifetime divorce rate of 39.5% for problem gamblers, compared to 18.2% for the general population. 80% of divorced problem gamblers cite gambling as a significant factor in the split.',
                source: 'https://www.mccaberussell.com/blog/divorcing-a-gambling-addict-what-you-should-know/'
            }
        ]
    };

    // State
    let currentCategory = null;
    let currentIndex = 0;
    let modalElement = null;

    // Create modal HTML
    function createModal()
    {
        if (document.getElementById('reality-modal')) return;

        const modal = document.createElement('div');
        modal.id = 'reality-modal';
        modal.className = 'fixed inset-0 z-[9999] hidden';
        modal.innerHTML = `
            <!-- Backdrop -->
            <div class="absolute inset-0 bg-black/70 backdrop-blur-sm" onclick="window.closeRealityModal()"></div>
            
            <!-- Modal Container -->
            <div class="absolute inset-4 md:inset-8 lg:inset-16 flex items-center justify-center pointer-events-none">
                <div class="relative bg-white dark:bg-slate-800 rounded-2xl shadow-2xl max-w-2xl w-full overflow-hidden pointer-events-auto flex flex-col" style="height: 400px;">
                    
                    <!-- Header -->
                    <div class="flex items-center justify-between p-4 border-b border-slate-200 dark:border-slate-700 shrink-0">
                        <h3 id="reality-modal-title" class="text-lg font-black text-red-600 uppercase tracking-wide"></h3>
                        <button onclick="window.closeRealityModal()" class="p-2 hover:bg-slate-100 dark:hover:bg-slate-700 rounded-full transition-colors">
                            <span class="material-symbols-outlined text-slate-500">close</span>
                        </button>
                    </div>
                    
                    <!-- Content -->
                    <div class="flex-1 overflow-y-auto p-6">
                        <p id="reality-modal-content" class="text-slate-700 dark:text-slate-300 leading-relaxed"></p>
                        <a id="reality-modal-source" href="#" target="_blank" class="mt-4 text-red-600 hover:text-red-700 font-bold text-sm flex items-center gap-1 hidden">
                            Source <span class="material-symbols-outlined text-sm">open_in_new</span>
                        </a>
                    </div>
                    
                    <!-- Footer with Navigation -->
                    <div class="flex items-center justify-between p-4 border-t border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-900/50 shrink-0">
                        <!-- Prev Button -->
                        <button id="reality-prev-btn" onclick="window.prevRealityItem()" class="btn-raised flex items-center justify-center gap-1 px-4 py-2 bg-red-600 hover:bg-red-700 text-white font-bold rounded-lg transition-all text-sm m-0 shrink-0 shadow-lg">
                            <span class="material-symbols-outlined text-[18px]">arrow_back_ios_new</span>
                            <span class="hidden sm:inline">Previous</span>
                        </button>
                        
                        <!-- Pagination -->
                        <span id="reality-modal-pagination" class="text-sm text-slate-500 dark:text-slate-400 font-medium"></span>
                        
                        <!-- Next Button -->
                        <button id="reality-next-btn" onclick="window.nextRealityItem()" class="btn-raised flex items-center justify-center gap-1 px-4 py-2 bg-red-600 hover:bg-red-700 text-white font-bold rounded-lg transition-all text-sm m-0 shrink-0 shadow-lg">
                            <span class="hidden sm:inline">Next</span>
                            <span class="material-symbols-outlined text-[18px]">arrow_forward_ios</span>
                        </button>
                    </div>
                </div>
            </div>
        `;
        document.body.appendChild(modal);
        modalElement = modal;

        // Keyboard navigation
        document.addEventListener('keydown', handleKeydown);
    }

    function handleKeydown(e)
    {
        if (!modalElement || modalElement.classList.contains('hidden')) return;

        if (e.key === 'Escape')
        {
            window.closeRealityModal();
        } else if (e.key === 'ArrowLeft')
        {
            window.prevRealityItem();
        } else if (e.key === 'ArrowRight')
        {
            window.nextRealityItem();
        }
    }

    function updateModalContent()
    {
        const items = REALITY_DATA[currentCategory];
        if (!items || !items[currentIndex]) return;

        const item = items[currentIndex];

        document.getElementById('reality-modal-title').textContent = item.title;
        document.getElementById('reality-modal-content').innerHTML = item.content;
        document.getElementById('reality-modal-pagination').textContent = `${currentIndex + 1} of ${items.length}`;

        // Source link
        const sourceEl = document.getElementById('reality-modal-source');
        if (item.source)
        {
            sourceEl.href = item.source;
            sourceEl.classList.remove('hidden');
        } else
        {
            sourceEl.classList.add('hidden');
        }

        // Navigation button states
        document.getElementById('reality-prev-btn').disabled = currentIndex === 0;
        document.getElementById('reality-next-btn').disabled = currentIndex === items.length - 1;

        document.getElementById('reality-prev-btn').classList.toggle('opacity-50', currentIndex === 0);
        document.getElementById('reality-next-btn').classList.toggle('opacity-50', currentIndex === items.length - 1);
    }

    // Public API
    window.openRealityModal = function (categoryId)
    {
        createModal();
        currentCategory = categoryId;
        currentIndex = 0;

        updateModalContent();
        modalElement.classList.remove('hidden');
        document.body.style.overflow = 'hidden';
    };

    window.closeRealityModal = function ()
    {
        if (modalElement)
        {
            modalElement.classList.add('hidden');
            document.body.style.overflow = '';
        }
    };

    window.nextRealityItem = function ()
    {
        const items = REALITY_DATA[currentCategory];
        if (items && currentIndex < items.length - 1)
        {
            currentIndex++;
            updateModalContent();
        }
    };

    window.prevRealityItem = function ()
    {
        if (currentIndex > 0)
        {
            currentIndex--;
            updateModalContent();
        }
    };
})();
