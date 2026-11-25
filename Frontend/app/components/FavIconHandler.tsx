import { useEffect } from "react";
import { koalaMarkSvgString } from "./KoalaMark";

/**
 * A utility component that dynamically generates and updates the browser's favicon
 * based on the application's primary theme color.
 *
 * This component performs the following steps:
 * 1. Reads the `--board-primary` CSS variable from the document root.
 * 2. Injects that color into a predefined SVG template.
 * 3. Converts the SVG string into a Blob URL.
 * 4. Updates or creates a `<link rel="icon">` tag in the document head.
 *
 * This ensures the brand identity is reflected even in browser tabs without
 * requiring multiple static image files.
 *
 * @component
 * @returns {null} This component does not render any visual UI elements.
 */
const FaviconHandler = () => {
  useEffect(() => {
    const mediaQuery = window.matchMedia("(prefers-color-scheme: dark)"); // darkmode???

     let currentUrl: string | null = null;

     // Theme event handler
     const updateFavicon = () => { 
      const rootStyle = getComputedStyle(document.documentElement);

      const primaryColor =
        rootStyle.getPropertyValue("--board-primary").trim() || "#f96a1f";

      const svgString = koalaMarkSvgString(
        primaryColor,
        mediaQuery.matches,
      );

      const blob = new Blob([svgString], {
        type: "image/svg+xml",
      });

      const url = URL.createObjectURL(blob);

      // This was a test for favicon bookmarks, but it didn't work in firefox and chromium. Maybe for a later solution
      // const dataUri = `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svgString)}`;

      let link = document.querySelector(
        "link[rel~='icon']",
      ) as HTMLLinkElement | null;

      if (!link) {
        link = document.createElement("link");
        link.rel = "icon";
        document.head.appendChild(link);
      }

      link.href = url;

      if (currentUrl) {
        URL.revokeObjectURL(currentUrl);
      }

      currentUrl = url;
    };

    updateFavicon();

    mediaQuery.addEventListener("change", updateFavicon);

    return () => {
      mediaQuery.removeEventListener("change", updateFavicon);

      if (currentUrl) {
        URL.revokeObjectURL(currentUrl);
      }
    };
  }, []);

  return null;
};

export default FaviconHandler;
