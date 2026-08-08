window.ImageModal = (function () {
    function init() {
        const imageModal = document.getElementById('image-modal');
        const modalImage = document.getElementById('modal-image');
        if (!imageModal || !modalImage) return;

        window.openImageModal = function (src) {
            modalImage.src = src;
            imageModal.classList.remove('opacity-0', 'pointer-events-none');
            document.body.classList.add('overflow-hidden');
            // Small timeout to allow the fade-in transition to start before scaling up
            setTimeout(() => {
                modalImage.classList.remove('scale-95');
                modalImage.classList.add('scale-100');
            }, 10);
        };

        window.closeImageModal = function () {
            imageModal.classList.add('opacity-0', 'pointer-events-none');
            modalImage.classList.remove('scale-100');
            modalImage.classList.add('scale-95');
            document.body.classList.remove('overflow-hidden');
        };
    }

    return {
        init: init
    };
})();
