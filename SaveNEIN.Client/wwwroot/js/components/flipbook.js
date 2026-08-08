// Flipbook Logic - Ported from static_html_to_convert/index.html
// Uses PDF.js to render pages + StPageFlip for animations

let flipbookInstance = null;

// Configure PDF.js worker when library loads
if (typeof pdfjsLib !== 'undefined')
{
    pdfjsLib.GlobalWorkerOptions.workerSrc = 'js/lib/pdf.worker.min.js';
}

window.openFlipbook = async function (e)
{
    if (e) e.preventDefault();

    const flipbookModal = document.getElementById('flipbook-modal');
    const bookEl = document.getElementById('flipbook');
    const loadingEl = document.getElementById('flipbook-loading');
    const pdfUrl = 'assets/Independent_Analysis_Allen_County_Casino.pdf';

    if (!flipbookModal || !bookEl || !loadingEl)
    {
        console.error('Flipbook elements not found - modal:', !!flipbookModal, 'book:', !!bookEl, 'loading:', !!loadingEl);
        return;
    }

    // Configure PDF.js worker (retry in case it wasn't set earlier)
    if (typeof pdfjsLib !== 'undefined' && !pdfjsLib.GlobalWorkerOptions.workerSrc)
    {
        pdfjsLib.GlobalWorkerOptions.workerSrc = 'js/lib/pdf.worker.min.js';
    }

    // Show Modal
    flipbookModal.classList.remove('opacity-0', 'pointer-events-none');
    document.body.classList.add('overflow-hidden');
    loadingEl.style.display = 'flex';
    bookEl.classList.add('hidden');

    // If already loaded, just show
    if (flipbookInstance && bookEl.children.length > 0)
    {
        loadingEl.style.display = 'none';
        bookEl.classList.remove('hidden');
        return;
    }

    try
    {
        const loadingTask = pdfjsLib.getDocument(pdfUrl);
        const pdf = await loadingTask.promise;
        const numPages = pdf.numPages;

        // Determine scale based on screen
        const isMobile = window.innerWidth < 768;
        const scale = window.devicePixelRatio || 1.5;

        bookEl.innerHTML = '';

        // Measure page 1 for aspect ratio
        const page1 = await pdf.getPage(1);
        const viewport1 = page1.getViewport({ scale: 1 });
        const aspectRatio = viewport1.width / viewport1.height;

        for (let i = 1; i <= numPages; i++)
        {
            const page = await pdf.getPage(i);
            const viewport = page.getViewport({ scale: scale * 1.5 });

            const pageContainer = document.createElement('div');
            pageContainer.className = 'page relative';

            const canvas = document.createElement('canvas');
            canvas.width = viewport.width;
            canvas.height = viewport.height;
            canvas.style.width = '100%';
            canvas.style.height = '100%';
            canvas.style.objectFit = 'contain';

            pageContainer.appendChild(canvas);

            // Annotation Layer (Links)
            try
            {
                const annotations = await page.getAnnotations();
                if (annotations && annotations.length > 0)
                {
                    const annotationLayer = document.createElement('div');
                    annotationLayer.className = 'absolute inset-0 pointer-events-none';

                    annotations.forEach(annotation =>
                    {
                        if (annotation.subtype === 'Link' && annotation.dest)
                        {
                            const rect = viewport.convertToViewportRectangle(annotation.rect);

                            const linkDiv = document.createElement('div');
                            linkDiv.className = 'absolute cursor-pointer hover:bg-yellow-500/20 transition-colors pointer-events-auto z-50';

                            linkDiv.style.left = `${(rect[0] / viewport.width) * 100}%`;
                            linkDiv.style.top = `${(rect[1] / viewport.height) * 100}%`;
                            linkDiv.style.width = `${((rect[2] - rect[0]) / viewport.width) * 100}%`;
                            linkDiv.style.height = `${((rect[3] - rect[1]) / viewport.height) * 100}%`;

                            const stopProp = (e) => { e.stopPropagation(); };
                            linkDiv.addEventListener('mousedown', stopProp);
                            linkDiv.addEventListener('touchstart', stopProp);
                            linkDiv.addEventListener('mouseup', stopProp);
                            linkDiv.addEventListener('touchend', stopProp);

                            linkDiv.onclick = async (ev) =>
                            {
                                ev.preventDefault();
                                ev.stopPropagation();

                                let dest = annotation.dest;
                                if (typeof dest === 'string')
                                {
                                    dest = await pdf.getDestination(dest);
                                }
                                if (Array.isArray(dest))
                                {
                                    const ref = dest[0];
                                    const pageIndex = await pdf.getPageIndex(ref);
                                    if (flipbookInstance)
                                    {
                                        flipbookInstance.flip(pageIndex);
                                    }
                                }
                            };
                            annotationLayer.appendChild(linkDiv);
                        }
                    });
                    pageContainer.appendChild(annotationLayer);
                }
            } catch (err)
            {
                console.warn('Error loading annotations:', err);
            }

            bookEl.appendChild(pageContainer);

            const renderContext = {
                canvasContext: canvas.getContext('2d'),
                viewport: viewport
            };
            page.render(renderContext);
        }

        // Initialize PageFlip with Dynamic Scalable Sizing
        // Instead of hardcoded pixels, we calculate the optimal dimensions to fit the viewport

        // 1. Define available space (leaving room for UI/padding)
        // Reduced from 0.80 to 0.70 to ensure pages fit within the container without clipping tops/bottoms
        const availableWidth = window.innerWidth * (isMobile ? 0.95 : 0.85);
        const availableHeight = window.innerHeight * 0.70;

        // 2. Calculate dimensions for a SINGLE page based on spread mode
        // Desktop = 2 pages side-by-side, so each page gets half the width
        const maxSinglePageWidth = isMobile ? availableWidth : (availableWidth / 2);

        // 3. fitting logic: Start by fitting to width
        let finalPageWidth = maxSinglePageWidth;
        let finalPageHeight = finalPageWidth / aspectRatio;

        // 4. If height overflows, scale down to fit height instead
        if (finalPageHeight > availableHeight)
        {
            finalPageHeight = availableHeight;
            finalPageWidth = finalPageHeight * aspectRatio;
        }

        // 5. Round to integers for cleaner rendering
        finalPageWidth = Math.floor(finalPageWidth);
        finalPageHeight = Math.floor(finalPageHeight);

        flipbookInstance = new St.PageFlip(bookEl, {
            width: finalPageWidth,
            height: finalPageHeight,

            size: 'stretch',

            // Relax constraints to allow our calculated size to work
            minWidth: 200,
            maxWidth: 2000,
            minHeight: 300,
            maxHeight: 2000,

            maxShadowOpacity: 0.5,
            showCover: true,
            mobileScrollSupport: true,

            usePortrait: isMobile,
            startPage: 0,
            autoSize: true,
            drawShadow: true
        });

        flipbookInstance.loadFromHTML(document.querySelectorAll('#flipbook .page'));

        loadingEl.style.display = 'none';
        bookEl.classList.remove('hidden');

        // Show Instructions
        const instructionEl = document.getElementById('flipbook-instructions');
        const instructionCard = document.getElementById('instruction-card');

        if (instructionEl && instructionCard)
        {
            instructionEl.classList.remove('opacity-0');
            instructionCard.classList.remove('scale-95');
            instructionCard.classList.add('scale-100');

            setTimeout(() =>
            {
                instructionEl.classList.add('opacity-0');
                instructionCard.classList.remove('scale-100');
                instructionCard.classList.add('scale-95');
            }, 3500);
        }

    } catch (err)
    {
        console.error('Flipbook error:', err);
        loadingEl.innerHTML = '<p class="text-white bg-red-500/20 p-4 rounded">Error loading PDF. Please try refreshing.</p>';
    }
};

window.closeFlipbook = function ()
{
    const flipbookModal = document.getElementById('flipbook-modal');
    if (flipbookModal)
    {
        flipbookModal.classList.add('opacity-0', 'pointer-events-none');
        document.body.classList.remove('overflow-hidden');
    }
};

// Navigation functions
window.flipbookPrev = function ()
{
    if (flipbookInstance) flipbookInstance.flipPrev();
};

window.flipbookNext = function ()
{
    if (flipbookInstance) flipbookInstance.flipNext();
};

window.downloadFlipbookPDF = function ()
{
    const link = document.createElement('a');
    link.href = 'assets/Independent_Analysis_Allen_County_Casino.pdf';
    link.download = 'Independent_Analysis_Allen_County_Casino.pdf';
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
};
