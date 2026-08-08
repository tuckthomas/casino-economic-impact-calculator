/** @type {import('tailwindcss').Config} */
module.exports = {
  content: ["./SaveNEIN.Client/**/*.{razor,html,js}"],
  darkMode: "class",
  theme: {
    extend: {
      colors: {
        "primary": "#0f172a",
        "primary-dark": "#020617",
        "accent": "#e11d48",
        "background-light": "#f8fafc",
        "background-dark": "#020617",
        "surface-light": "#ffffff",
        "surface-dark": "#1e293b",
        "nein-teal": "#0f172a",
        "nein-teal-dark": "#020617",
        "fw-teal": "#0f172a",
        "fw-teal-dark": "#020617",
      },
      fontFamily: {
        "display": ["'Public Sans Variable'", "Public Sans", "sans-serif"]
      },
      backgroundImage: {
        'hero-gradient': 'linear-gradient(to right bottom, #0f172a, #1e293b, #334155)',
      },
      animation: {
        'loop-pre': 'loopPre 10s ease-in-out infinite',
        'loop-post': 'loopPost 10s ease-in-out infinite',
      },
      keyframes: {
        loopPre: {
          '0%, 45%': { opacity: '1', zIndex: '20', pointerEvents: 'auto' },
          '55%, 95%': { opacity: '0', zIndex: '0', pointerEvents: 'none' },
          '100%': { opacity: '1', zIndex: '20', pointerEvents: 'auto' },
        },
        loopPost: {
          '0%, 45%': { opacity: '0', zIndex: '0', pointerEvents: 'none' },
          '55%, 95%': { opacity: '1', zIndex: '20', pointerEvents: 'auto' },
          '100%': { opacity: '0', zIndex: '0', pointerEvents: 'none' },
        }
      }
    },
  },
  plugins: [
    require('@tailwindcss/forms'),
    require('@tailwindcss/container-queries'),
  ],
}
