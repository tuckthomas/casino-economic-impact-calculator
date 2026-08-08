/**
 * AdaptiveToast - A reusable toast notification component
 * Features:
 * - Auto-positions to best corner or within a container
 * - ToastTitle (bold) and ToastMessage (regular)
 * - Close button (X)
 * - Auto-dismiss with configurable duration
 * - Smooth animations
 */

window.AdaptiveToast = (function ()
{
    let toastContainer = null;
    let activeToasts = [];

    const TOAST_DURATION = 5000; // 5 seconds default
    const ANIMATION_DURATION = 300; // 300ms for slide in/out

    // Determine best corner based on viewport and optional container
    function getBestPosition(container)
    {
        if (container)
        {
            // Position within container (top-right of container)
            return { mode: 'container', element: container };
        }

        // Default: top-right of viewport
        return { mode: 'viewport', corner: 'top-right' };
    }

    function createToastContainer(container)
    {
        if (toastContainer && document.body.contains(toastContainer))
        {
            return toastContainer;
        }

        toastContainer = document.createElement('div');
        toastContainer.id = 'adaptive-toast-container';
        toastContainer.style.cssText = `
            position: absolute;
            top: 12px;
            right: 12px;
            z-index: 9999;
            display: flex;
            flex-direction: column;
            gap: 8px;
            max-width: 320px;
            pointer-events: none;
        `;

        if (container)
        {
            // Ensure container has relative positioning
            const containerStyle = getComputedStyle(container);
            if (containerStyle.position === 'static')
            {
                container.style.position = 'relative';
            }
            container.appendChild(toastContainer);
        } else
        {
            document.body.appendChild(toastContainer);
            toastContainer.style.position = 'fixed';
        }

        return toastContainer;
    }

    function createToastElement(title, message, type = 'info')
    {
        const toast = document.createElement('div');
        toast.className = 'adaptive-toast';

        // Neutral dark styling to match site theme
        toast.style.cssText = `
            background: rgba(30, 41, 59, 0.95);
            border: 1px solid rgba(100, 116, 139, 0.3);
            border-radius: 8px;
            padding: 12px 16px;
            color: white;
            font-family: 'Public Sans Variable', system-ui, sans-serif;
            font-size: 14px;
            box-shadow: 0 4px 20px rgba(0, 0, 0, 0.3);
            backdrop-filter: blur(8px);
            pointer-events: auto;
            transform: translateX(120%);
            transition: transform ${ANIMATION_DURATION}ms ease-out, opacity ${ANIMATION_DURATION}ms ease-out;
            opacity: 0;
            display: flex;
            flex-direction: column;
            gap: 4px;
            position: relative;
        `;

        // Close button
        const closeBtn = document.createElement('button');
        closeBtn.innerHTML = '×';
        closeBtn.style.cssText = `
            position: absolute;
            top: 6px;
            right: 8px;
            background: none;
            border: none;
            color: rgba(255, 255, 255, 0.7);
            font-size: 20px;
            line-height: 1;
            cursor: pointer;
            padding: 0;
            width: 20px;
            height: 20px;
            display: flex;
            align-items: center;
            justify-content: center;
            transition: color 0.2s;
        `;
        closeBtn.onmouseenter = () => closeBtn.style.color = 'white';
        closeBtn.onmouseleave = () => closeBtn.style.color = 'rgba(255, 255, 255, 0.7)';

        // Title
        const titleEl = document.createElement('div');
        titleEl.style.cssText = 'font-weight: 600; padding-right: 20px;';
        titleEl.textContent = title;

        // Message
        const messageEl = document.createElement('div');
        messageEl.style.cssText = 'font-weight: 400; opacity: 0.9; padding-right: 20px;';
        messageEl.textContent = message;

        toast.appendChild(closeBtn);
        toast.appendChild(titleEl);
        if (message) toast.appendChild(messageEl);

        return { toast, closeBtn };
    }

    function show(title, message, options = {})
    {
        const {
            type = 'info',
            duration = TOAST_DURATION,
            container = null
        } = options;

        const toastContainer = createToastContainer(container);
        const { toast, closeBtn } = createToastElement(title, message, type);

        toastContainer.appendChild(toast);
        activeToasts.push(toast);

        // Trigger animation after a frame
        requestAnimationFrame(() =>
        {
            toast.style.transform = 'translateX(0)';
            toast.style.opacity = '1';
        });

        // Auto-dismiss
        let dismissTimeout = setTimeout(() => dismiss(toast), duration);

        // Close button handler
        closeBtn.onclick = () =>
        {
            clearTimeout(dismissTimeout);
            dismiss(toast);
        };

        // Pause on hover
        toast.onmouseenter = () => clearTimeout(dismissTimeout);
        toast.onmouseleave = () =>
        {
            dismissTimeout = setTimeout(() => dismiss(toast), 2000);
        };

        return toast;
    }

    function dismiss(toast)
    {
        if (!toast || !document.body.contains(toast)) return;

        toast.style.transform = 'translateX(120%)';
        toast.style.opacity = '0';

        setTimeout(() =>
        {
            toast.remove();
            activeToasts = activeToasts.filter(t => t !== toast);

            // Remove container if empty
            if (activeToasts.length === 0 && toastContainer)
            {
                toastContainer.remove();
                toastContainer = null;
            }
        }, ANIMATION_DURATION);
    }

    function dismissAll()
    {
        activeToasts.forEach(dismiss);
    }

    // Public API
    return {
        show,
        dismiss,
        dismissAll,
        info: (title, message, options) => show(title, message, { ...options, type: 'info' }),
        warning: (title, message, options) => show(title, message, { ...options, type: 'warning' }),
        error: (title, message, options) => show(title, message, { ...options, type: 'error' }),
        success: (title, message, options) => show(title, message, { ...options, type: 'success' })
    };
})();
