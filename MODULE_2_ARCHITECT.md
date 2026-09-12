# TexTrack ERP — Module 2 Architecture Blueprint

## Module identity

**Module 2: Material In Voucher Hardening and Searchable Voucher Entry**

This module completes the Material In workflow without redesigning unrelated vouchers. Its purpose is to make Material In practical for large datasets, safe for alteration, cancellable and conditionally deletable, while preserving the shared TexTrack voucher keyboard and visual contracts.

The implementation must solve these confirmed defects:

- Party A/c Name cannot currently be searched by typing.
- JWO / Order No. cannot currently be searched by typing.
- Consumption Godown and Receiving Godown cannot currently be searched by typing.
- Material In has cancellation but no Delete command.
- Alteration must display the original Party and JWO while preventing their replacement.
- Mouse, arrow-key and Enter selection must all work.
- No Material In operation may partially change stock, allocations or voucher data.

This document is an implementation specification. It is not permission to change Material Out, JWO, reports, process-cost formulas, Backspace logic, global shortcut semantics or unrelated visuals.

## Existing technical baseline
<!DOCTYPE html>
<!-- saved from url=(0048)file:///C:/Users/VICTUS/Desktop/!DOCTYPE%20.html -->
<html class="light" lang="en" style=""><head><meta http-equiv="Content-Type" content="text/html; charset=UTF-8">

<meta content="width=device-width, initial-scale=1.0" name="viewport">
<title>Material In - TexTrack ERP</title>
<script src="./Material In - TexTrack ERP_files/saved_resource"></script>
<link href="./Material In - TexTrack ERP_files/css2" rel="stylesheet">
<link href="./Material In - TexTrack ERP_files/css2(1)" rel="stylesheet">
<script id="tailwind-config">
        tailwind.config = {
            darkMode: "class",
            theme: {
                extend: {
                    "colors": {
                        "on-background": "#1b1b1d",
                        "secondary-fixed-dim": "#b7c8e1",
                        "on-surface-variant": "#45474c",
                        "error-container": "#ffdad6",
                        "tertiary-fixed": "#fadfb8",
                        "primary-container": "#1e293b",
                        "surface-container-low": "#f5f3f4",
                        "primary": "#091426",
                        "surface-variant": "#e4e2e3",
                        "on-secondary-fixed": "#0b1c30",
                        "inverse-primary": "#bcc7de",
                        "tertiary": "#1e1200",
                        "surface-tint": "#545f73",
                        "on-error-container": "#93000a",
                        "surface-container-high": "#eae7e9",
                        "outline": "#75777d",
                        "on-primary": "#ffffff",
                        "surface-bright": "#fbf8fa",
                        "on-tertiary-fixed": "#271902",
                        "on-primary-fixed-variant": "#3c475a",
                        "outline-variant": "#c5c6cd",
                        "inverse-on-surface": "#f3f0f2",
                        "inverse-surface": "#303032",
                        "error": "#ba1a1a",
                        "surface-container-highest": "#e4e2e3",
                        "on-tertiary-fixed-variant": "#564427",
                        "on-surface": "#1b1b1d",
                        "on-primary-container": "#8590a6",
                        "primary-fixed-dim": "#bcc7de",
                        "on-secondary-fixed-variant": "#38485d",
                        "tertiary-container": "#35260c",
                        "tertiary-fixed-dim": "#ddc39d",
                        "secondary-fixed": "#d3e4fe",
                        "secondary-container": "#d0e1fb",
                        "on-error": "#ffffff",
                        "surface-container-lowest": "#ffffff",
                        "on-tertiary": "#ffffff",
                        "on-secondary": "#ffffff",
                        "primary-fixed": "#d8e3fb",
                        "on-secondary-container": "#54647a",
                        "surface-dim": "#dcd9db",
                        "secondary": "#505f76",
                        "on-primary-fixed": "#111c2d",
                        "background": "#fbf8fa",
                        "surface-container": "#f0edef",
                        "surface": "#fbf8fa",
                        "on-tertiary-container": "#a38c6a"
                    },
                    "borderRadius": {
                        "DEFAULT": "0.125rem",
                        "lg": "0.25rem",
                        "xl": "0.5rem",
                        "full": "0.75rem"
                    },
                    "spacing": {
                        "gutter": "16px",
                        "table-cell-padding": "10px",
                        "unit": "4px",
                        "margin-page": "24px",
                        "input-padding-y": "8px",
                        "input-padding-x": "12px"
                    },
                    "fontFamily": {
                        "headline-lg": ["Inter"],
                        "mono-data": ["JetBrains Mono"],
                        "body-sm": ["Inter"],
                        "table-data": ["Inter"],
                        "label-bold": ["Inter"],
                        "label-md": ["Inter"],
                        "headline-md": ["Inter"],
                        "body-md": ["Inter"]
                    },
                    "fontSize": {
                        "headline-lg": ["24px", {"lineHeight": "32px", "letterSpacing": "-0.02em", "fontWeight": "600"}],
                        "mono-data": ["11px", {"lineHeight": "14px", "fontWeight": "450"}],
                        "body-sm": ["11px", {"lineHeight": "14px", "fontWeight": "400"}],
                        "table-data": ["11px", {"lineHeight": "14px", "fontWeight": "400"}],
                        "label-bold": ["11px", {"lineHeight": "14px", "letterSpacing": "0.05em", "fontWeight": "600"}],
                        "label-md": ["11px", {"lineHeight": "14px", "fontWeight": "500"}],
                        "headline-md": ["14px", {"lineHeight": "18px", "fontWeight": "600"}],
                        "body-md": ["12px", {"lineHeight": "16px", "fontWeight": "400"}]
                    }
                },
            },
        }
    </script>
<style>
        .material-symbols-outlined {
            font-variation-settings: 'FILL' 0, 'wght' 400, 'GRAD' 0, 'opsz' 20;
            vertical-align: middle;
            font-size: 16px;
        }
        body { font-family: 'Inter', sans-serif; background-color: #fbf8fa; color: #1b1b1d; font-size: 11px; }
        .tally-border { border: 1px solid #c5c6cd; }
        .tally-input { 
            border: 1px solid #75777d; 
            border-radius: 0.125rem; 
            height: 20px; 
            padding: 0 4px;
            font-size: 11px;
            transition: all 0.1s ease;
        }
        .tally-input:focus { 
            outline: none; 
            border-color: #091426;
            box-shadow: 0 0 0 1px rgba(9, 20, 38, 0.2);
            background-color: #ffffff;
        }
        .zebra-table tr:nth-child(even) { background-color: #f5f3f4; }
        .zebra-table tr:hover { background-color: #f0edef !important; }
        th { font-weight: 700; color: #45474c; border-bottom: 1px solid #c5c6cd; }
        
        .nav-link { position: relative; padding-bottom: 2px; }
        .nav-link::after {
            content: '';
            position: absolute;
            bottom: -2px;
            left: 0;
            width: 0;
            height: 2px;
            background-color: #ffffff;
            transition: width 0.2s ease;
        }
        .nav-link.active::after { width: 100%; }
        
        @keyframes pulse-glow {
            0% { transform: scale(1); opacity: 1; box-shadow: 0 0 0 0 rgba(52, 211, 153, 0.4); }
            70% { transform: scale(1); opacity: 1; box-shadow: 0 0 0 6px rgba(52, 211, 153, 0); }
            100% { transform: scale(1); opacity: 1; box-shadow: 0 0 0 0 rgba(52, 211, 153, 0); }
        }
        .pulse-indicator { animation: pulse-glow 2s infinite; }
    </style>
<style>*, ::before, ::after{--tw-border-spacing-x:0;--tw-border-spacing-y:0;--tw-translate-x:0;--tw-translate-y:0;--tw-rotate:0;--tw-skew-x:0;--tw-skew-y:0;--tw-scale-x:1;--tw-scale-y:1;--tw-pan-x: ;--tw-pan-y: ;--tw-pinch-zoom: ;--tw-scroll-snap-strictness:proximity;--tw-gradient-from-position: ;--tw-gradient-via-position: ;--tw-gradient-to-position: ;--tw-ordinal: ;--tw-slashed-zero: ;--tw-numeric-figure: ;--tw-numeric-spacing: ;--tw-numeric-fraction: ;--tw-ring-inset: ;--tw-ring-offset-width:0px;--tw-ring-offset-color:#fff;--tw-ring-color:rgb(59 130 246 / 0.5);--tw-ring-offset-shadow:0 0 #0000;--tw-ring-shadow:0 0 #0000;--tw-shadow:0 0 #0000;--tw-shadow-colored:0 0 #0000;--tw-blur: ;--tw-brightness: ;--tw-contrast: ;--tw-grayscale: ;--tw-hue-rotate: ;--tw-invert: ;--tw-saturate: ;--tw-sepia: ;--tw-drop-shadow: ;--tw-backdrop-blur: ;--tw-backdrop-brightness: ;--tw-backdrop-contrast: ;--tw-backdrop-grayscale: ;--tw-backdrop-hue-rotate: ;--tw-backdrop-invert: ;--tw-backdrop-opacity: ;--tw-backdrop-saturate: ;--tw-backdrop-sepia: ;--tw-contain-size: ;--tw-contain-layout: ;--tw-contain-paint: ;--tw-contain-style: }::backdrop{--tw-border-spacing-x:0;--tw-border-spacing-y:0;--tw-translate-x:0;--tw-translate-y:0;--tw-rotate:0;--tw-skew-x:0;--tw-skew-y:0;--tw-scale-x:1;--tw-scale-y:1;--tw-pan-x: ;--tw-pan-y: ;--tw-pinch-zoom: ;--tw-scroll-snap-strictness:proximity;--tw-gradient-from-position: ;--tw-gradient-via-position: ;--tw-gradient-to-position: ;--tw-ordinal: ;--tw-slashed-zero: ;--tw-numeric-figure: ;--tw-numeric-spacing: ;--tw-numeric-fraction: ;--tw-ring-inset: ;--tw-ring-offset-width:0px;--tw-ring-offset-color:#fff;--tw-ring-color:rgb(59 130 246 / 0.5);--tw-ring-offset-shadow:0 0 #0000;--tw-ring-shadow:0 0 #0000;--tw-shadow:0 0 #0000;--tw-shadow-colored:0 0 #0000;--tw-blur: ;--tw-brightness: ;--tw-contrast: ;--tw-grayscale: ;--tw-hue-rotate: ;--tw-invert: ;--tw-saturate: ;--tw-sepia: ;--tw-drop-shadow: ;--tw-backdrop-blur: ;--tw-backdrop-brightness: ;--tw-backdrop-contrast: ;--tw-backdrop-grayscale: ;--tw-backdrop-hue-rotate: ;--tw-backdrop-invert: ;--tw-backdrop-opacity: ;--tw-backdrop-saturate: ;--tw-backdrop-sepia: ;--tw-contain-size: ;--tw-contain-layout: ;--tw-contain-paint: ;--tw-contain-style: }/* ! tailwindcss v3.4.17 | MIT License | https://tailwindcss.com */*,::after,::before{box-sizing:border-box;border-width:0;border-style:solid;border-color:#e5e7eb}::after,::before{--tw-content:''}:host,html{line-height:1.5;-webkit-text-size-adjust:100%;-moz-tab-size:4;tab-size:4;font-family:ui-sans-serif, system-ui, sans-serif, "Apple Color Emoji", "Segoe UI Emoji", "Segoe UI Symbol", "Noto Color Emoji";font-feature-settings:normal;font-variation-settings:normal;-webkit-tap-highlight-color:transparent}body{margin:0;line-height:inherit}hr{height:0;color:inherit;border-top-width:1px}abbr:where([title]){-webkit-text-decoration:underline dotted;text-decoration:underline dotted}h1,h2,h3,h4,h5,h6{font-size:inherit;font-weight:inherit}a{color:inherit;text-decoration:inherit}b,strong{font-weight:bolder}code,kbd,pre,samp{font-family:ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", "Courier New", monospace;font-feature-settings:normal;font-variation-settings:normal;font-size:1em}small{font-size:80%}sub,sup{font-size:75%;line-height:0;position:relative;vertical-align:baseline}sub{bottom:-.25em}sup{top:-.5em}table{text-indent:0;border-color:inherit;border-collapse:collapse}button,input,optgroup,select,textarea{font-family:inherit;font-feature-settings:inherit;font-variation-settings:inherit;font-size:100%;font-weight:inherit;line-height:inherit;letter-spacing:inherit;color:inherit;margin:0;padding:0}button,select{text-transform:none}button,input:where([type=button]),input:where([type=reset]),input:where([type=submit]){-webkit-appearance:button;background-color:transparent;background-image:none}:-moz-focusring{outline:auto}:-moz-ui-invalid{box-shadow:none}progress{vertical-align:baseline}::-webkit-inner-spin-button,::-webkit-outer-spin-button{height:auto}[type=search]{-webkit-appearance:textfield;outline-offset:-2px}::-webkit-search-decoration{-webkit-appearance:none}::-webkit-file-upload-button{-webkit-appearance:button;font:inherit}summary{display:list-item}blockquote,dd,dl,figure,h1,h2,h3,h4,h5,h6,hr,p,pre{margin:0}fieldset{margin:0;padding:0}legend{padding:0}menu,ol,ul{list-style:none;margin:0;padding:0}dialog{padding:0}textarea{resize:vertical}input::placeholder,textarea::placeholder{opacity:1;color:#9ca3af}[role=button],button{cursor:pointer}:disabled{cursor:default}audio,canvas,embed,iframe,img,object,svg,video{display:block;vertical-align:middle}img,video{max-width:100%;height:auto}[hidden]:where(:not([hidden=until-found])){display:none}[type='text'],input:where(:not([type])),[type='email'],[type='url'],[type='password'],[type='number'],[type='date'],[type='datetime-local'],[type='month'],[type='search'],[type='tel'],[type='time'],[type='week'],[multiple],textarea,select{-webkit-appearance:none;appearance:none;background-color:#fff;border-color:#6b7280;border-width:1px;border-radius:0px;padding-top:0.5rem;padding-right:0.75rem;padding-bottom:0.5rem;padding-left:0.75rem;font-size:1rem;line-height:1.5rem;--tw-shadow:0 0 #0000;}[type='text']:focus, input:where(:not([type])):focus, [type='email']:focus, [type='url']:focus, [type='password']:focus, [type='number']:focus, [type='date']:focus, [type='datetime-local']:focus, [type='month']:focus, [type='search']:focus, [type='tel']:focus, [type='time']:focus, [type='week']:focus, [multiple]:focus, textarea:focus, select:focus{outline:2px solid transparent;outline-offset:2px;--tw-ring-inset:var(--tw-empty,/*!*/ /*!*/);--tw-ring-offset-width:0px;--tw-ring-offset-color:#fff;--tw-ring-color:#2563eb;--tw-ring-offset-shadow:var(--tw-ring-inset) 0 0 0 var(--tw-ring-offset-width) var(--tw-ring-offset-color);--tw-ring-shadow:var(--tw-ring-inset) 0 0 0 calc(1px + var(--tw-ring-offset-width)) var(--tw-ring-color);box-shadow:var(--tw-ring-offset-shadow), var(--tw-ring-shadow), var(--tw-shadow);border-color:#2563eb}input::placeholder,textarea::placeholder{color:#6b7280;opacity:1}::-webkit-datetime-edit-fields-wrapper{padding:0}::-webkit-date-and-time-value{min-height:1.5em;text-align:inherit}::-webkit-datetime-edit{display:inline-flex}::-webkit-datetime-edit,::-webkit-datetime-edit-year-field,::-webkit-datetime-edit-month-field,::-webkit-datetime-edit-day-field,::-webkit-datetime-edit-hour-field,::-webkit-datetime-edit-minute-field,::-webkit-datetime-edit-second-field,::-webkit-datetime-edit-millisecond-field,::-webkit-datetime-edit-meridiem-field{padding-top:0;padding-bottom:0}select{background-image:url("data:image/svg+xml,%3csvg xmlns='http://www.w3.org/2000/svg' fill='none' viewBox='0 0 20 20'%3e%3cpath stroke='%236b7280' stroke-linecap='round' stroke-linejoin='round' stroke-width='1.5' d='M6 8l4 4 4-4'/%3e%3c/svg%3e");background-position:right 0.5rem center;background-repeat:no-repeat;background-size:1.5em 1.5em;padding-right:2.5rem;print-color-adjust:exact}[multiple],[size]:where(select:not([size="1"])){background-image:initial;background-position:initial;background-repeat:unset;background-size:initial;padding-right:0.75rem;print-color-adjust:unset}[type='checkbox'],[type='radio']{-webkit-appearance:none;appearance:none;padding:0;print-color-adjust:exact;display:inline-block;vertical-align:middle;background-origin:border-box;-webkit-user-select:none;user-select:none;flex-shrink:0;height:1rem;width:1rem;color:#2563eb;background-color:#fff;border-color:#6b7280;border-width:1px;--tw-shadow:0 0 #0000}[type='checkbox']{border-radius:0px}[type='radio']{border-radius:100%}[type='checkbox']:focus,[type='radio']:focus{outline:2px solid transparent;outline-offset:2px;--tw-ring-inset:var(--tw-empty,/*!*/ /*!*/);--tw-ring-offset-width:2px;--tw-ring-offset-color:#fff;--tw-ring-color:#2563eb;--tw-ring-offset-shadow:var(--tw-ring-inset) 0 0 0 var(--tw-ring-offset-width) var(--tw-ring-offset-color);--tw-ring-shadow:var(--tw-ring-inset) 0 0 0 calc(2px + var(--tw-ring-offset-width)) var(--tw-ring-color);box-shadow:var(--tw-ring-offset-shadow), var(--tw-ring-shadow), var(--tw-shadow)}[type='checkbox']:checked,[type='radio']:checked{border-color:transparent;background-color:currentColor;background-size:100% 100%;background-position:center;background-repeat:no-repeat}[type='checkbox']:checked{background-image:url("data:image/svg+xml,%3csvg viewBox='0 0 16 16' fill='white' xmlns='http://www.w3.org/2000/svg'%3e%3cpath d='M12.207 4.793a1 1 0 010 1.414l-5 5a1 1 0 01-1.414 0l-2-2a1 1 0 011.414-1.414L6.5 9.086l4.293-4.293a1 1 0 011.414 0z'/%3e%3c/svg%3e");}@media (forced-colors: active) {[type='checkbox']:checked{-webkit-appearance:auto;appearance:auto}}[type='radio']:checked{background-image:url("data:image/svg+xml,%3csvg viewBox='0 0 16 16' fill='white' xmlns='http://www.w3.org/2000/svg'%3e%3ccircle cx='8' cy='8' r='3'/%3e%3c/svg%3e");}@media (forced-colors: active) {[type='radio']:checked{-webkit-appearance:auto;appearance:auto}}[type='checkbox']:checked:hover,[type='checkbox']:checked:focus,[type='radio']:checked:hover,[type='radio']:checked:focus{border-color:transparent;background-color:currentColor}[type='checkbox']:indeterminate{background-image:url("data:image/svg+xml,%3csvg xmlns='http://www.w3.org/2000/svg' fill='none' viewBox='0 0 16 16'%3e%3cpath stroke='white' stroke-linecap='round' stroke-linejoin='round' stroke-width='2' d='M4 8h8'/%3e%3c/svg%3e");border-color:transparent;background-color:currentColor;background-size:100% 100%;background-position:center;background-repeat:no-repeat;}@media (forced-colors: active) {[type='checkbox']:indeterminate{-webkit-appearance:auto;appearance:auto}}[type='checkbox']:indeterminate:hover,[type='checkbox']:indeterminate:focus{border-color:transparent;background-color:currentColor}[type='file']{background:unset;border-color:inherit;border-width:0;border-radius:0;padding:0;font-size:unset;line-height:inherit}[type='file']:focus{outline:1px solid ButtonText;outline:1px auto -webkit-focus-ring-color}.fixed{position:fixed}.sticky{position:sticky}.bottom-0{bottom:0px}.left-0{left:0px}.top-0{top:0px}.z-50{z-index:50}.col-span-3{grid-column:span 3 / span 3}.col-span-4{grid-column:span 4 / span 4}.col-span-8{grid-column:span 8 / span 8}.col-span-9{grid-column:span 9 / span 9}.mb-1{margin-bottom:0.25rem}.mb-12{margin-bottom:3rem}.ml-2{margin-left:0.5rem}.mr-1{margin-right:0.25rem}.mr-3{margin-right:0.75rem}.mt-1{margin-top:0.25rem}.flex{display:flex}.grid{display:grid}.h-1\.5{height:0.375rem}.h-10{height:2.5rem}.h-12{height:3rem}.h-3{height:0.75rem}.h-6{height:1.5rem}.h-\[24px\]{height:24px}.h-full{height:100%}.w-1\.5{width:0.375rem}.w-32{width:8rem}.w-\[11\%\]{width:11%}.w-\[12\%\]{width:12%}.w-\[1px\]{width:1px}.w-\[20\%\]{width:20%}.w-\[25\%\]{width:25%}.w-\[35\%\]{width:35%}.w-\[6\%\]{width:6%}.w-\[8\%\]{width:8%}.w-\[9\%\]{width:9%}.w-full{width:100%}.flex-1{flex:1 1 0%}.shrink-0{flex-shrink:0}.flex-grow{flex-grow:1}.table-fixed{table-layout:fixed}.border-collapse{border-collapse:collapse}.cursor-pointer{cursor:pointer}.select-none{-webkit-user-select:none;user-select:none}.resize-none{resize:none}.grid-cols-12{grid-template-columns:repeat(12, minmax(0, 1fr))}.grid-cols-2{grid-template-columns:repeat(2, minmax(0, 1fr))}.flex-col{flex-direction:column}.items-start{align-items:flex-start}.items-end{align-items:flex-end}.items-center{align-items:center}.justify-end{justify-content:flex-end}.justify-center{justify-content:center}.justify-between{justify-content:space-between}.gap-1{gap:0.25rem}.gap-1\.5{gap:0.375rem}.gap-2{gap:0.5rem}.gap-3{gap:0.75rem}.gap-4{gap:1rem}.gap-6{gap:1.5rem}.gap-x-8{column-gap:2rem}.gap-y-1{row-gap:0.25rem}.space-x-1\.5 > :not([hidden]) ~ :not([hidden]){--tw-space-x-reverse:0;margin-right:calc(0.375rem * var(--tw-space-x-reverse));margin-left:calc(0.375rem * calc(1 - var(--tw-space-x-reverse)))}.space-x-2 > :not([hidden]) ~ :not([hidden]){--tw-space-x-reverse:0;margin-right:calc(0.5rem * var(--tw-space-x-reverse));margin-left:calc(0.5rem * calc(1 - var(--tw-space-x-reverse)))}.space-x-4 > :not([hidden]) ~ :not([hidden]){--tw-space-x-reverse:0;margin-right:calc(1rem * var(--tw-space-x-reverse));margin-left:calc(1rem * calc(1 - var(--tw-space-x-reverse)))}.space-x-5 > :not([hidden]) ~ :not([hidden]){--tw-space-x-reverse:0;margin-right:calc(1.25rem * var(--tw-space-x-reverse));margin-left:calc(1.25rem * calc(1 - var(--tw-space-x-reverse)))}.space-x-6 > :not([hidden]) ~ :not([hidden]){--tw-space-x-reverse:0;margin-right:calc(1.5rem * var(--tw-space-x-reverse));margin-left:calc(1.5rem * calc(1 - var(--tw-space-x-reverse)))}.divide-y > :not([hidden]) ~ :not([hidden]){--tw-divide-y-reverse:0;border-top-width:calc(1px * calc(1 - var(--tw-divide-y-reverse)));border-bottom-width:calc(1px * var(--tw-divide-y-reverse))}.divide-outline-variant\/10 > :not([hidden]) ~ :not([hidden]){border-color:rgb(197 198 205 / 0.1)}.divide-surface-container-low > :not([hidden]) ~ :not([hidden]){--tw-divide-opacity:1;border-color:rgb(245 243 244 / var(--tw-divide-opacity, 1))}.overflow-hidden{overflow:hidden}.overflow-x-hidden{overflow-x:hidden}.rounded-full{border-radius:0.75rem}.rounded-sm{border-radius:0.125rem}.border{border-width:1px}.border-x{border-left-width:1px;border-right-width:1px}.border-b{border-bottom-width:1px}.border-b-2{border-bottom-width:2px}.border-l{border-left-width:1px}.border-r{border-right-width:1px}.border-t{border-top-width:1px}.border-none{border-style:none}.border-on-primary{--tw-border-opacity:1;border-color:rgb(255 255 255 / var(--tw-border-opacity, 1))}.border-outline{--tw-border-opacity:1;border-color:rgb(117 119 125 / var(--tw-border-opacity, 1))}.border-outline-variant{--tw-border-opacity:1;border-color:rgb(197 198 205 / var(--tw-border-opacity, 1))}.border-outline-variant\/30{border-color:rgb(197 198 205 / 0.3)}.bg-emerald-400{--tw-bg-opacity:1;background-color:rgb(52 211 153 / var(--tw-bg-opacity, 1))}.bg-on-primary\/20{background-color:rgb(255 255 255 / 0.2)}.bg-primary{--tw-bg-opacity:1;background-color:rgb(9 20 38 / var(--tw-bg-opacity, 1))}.bg-primary\/10{background-color:rgb(9 20 38 / 0.1)}.bg-surface{--tw-bg-opacity:1;background-color:rgb(251 248 250 / var(--tw-bg-opacity, 1))}.bg-surface-container-high{--tw-bg-opacity:1;background-color:rgb(234 231 233 / var(--tw-bg-opacity, 1))}.bg-surface-container-low{--tw-bg-opacity:1;background-color:rgb(245 243 244 / var(--tw-bg-opacity, 1))}.bg-surface-container-low\/50{background-color:rgb(245 243 244 / 0.5)}.bg-surface-container-lowest{--tw-bg-opacity:1;background-color:rgb(255 255 255 / var(--tw-bg-opacity, 1))}.bg-white{--tw-bg-opacity:1;background-color:rgb(255 255 255 / var(--tw-bg-opacity, 1))}.p-2{padding:0.5rem}.px-1\.5{padding-left:0.375rem;padding-right:0.375rem}.px-2{padding-left:0.5rem;padding-right:0.5rem}.px-4{padding-left:1rem;padding-right:1rem}.px-5{padding-left:1.25rem;padding-right:1.25rem}.px-margin-page{padding-left:24px;padding-right:24px}.py-0\.5{padding-top:0.125rem;padding-bottom:0.125rem}.py-1{padding-top:0.25rem;padding-bottom:0.25rem}.py-1\.5{padding-top:0.375rem;padding-bottom:0.375rem}.pb-0\.5{padding-bottom:0.125rem}.pb-1{padding-bottom:0.25rem}.pl-1{padding-left:0.25rem}.pl-2{padding-left:0.5rem}.pl-4{padding-left:1rem}.pt-3{padding-top:0.75rem}.text-left{text-align:left}.text-center{text-align:center}.text-right{text-align:right}.font-headline-md{font-family:Inter}.font-label-bold{font-family:Inter}.font-mono-data{font-family:JetBrains Mono}.\!text-\[13px\]{font-size:13px !important}.\!text-\[14px\]{font-size:14px !important}.text-\[10px\]{font-size:10px}.text-\[11px\]{font-size:11px}.text-\[12px\]{font-size:12px}.text-\[24px\]{font-size:24px}.text-\[9px\]{font-size:9px}.text-body-sm{font-size:11px;line-height:14px;font-weight:400}.text-headline-md{font-size:14px;line-height:18px;font-weight:600}.text-label-bold{font-size:11px;line-height:14px;letter-spacing:0.05em;font-weight:600}.text-label-md{font-size:11px;line-height:14px;font-weight:500}.font-black{font-weight:900}.font-bold{font-weight:700}.font-medium{font-weight:500}.font-semibold{font-weight:600}.uppercase{text-transform:uppercase}.italic{font-style:italic}.leading-tight{line-height:1.25}.tracking-tighter{letter-spacing:-0.05em}.tracking-wider{letter-spacing:0.05em}.tracking-widest{letter-spacing:0.1em}.text-emerald-600{--tw-text-opacity:1;color:rgb(5 150 105 / var(--tw-text-opacity, 1))}.text-on-primary{--tw-text-opacity:1;color:rgb(255 255 255 / var(--tw-text-opacity, 1))}.text-on-primary\/80{color:rgb(255 255 255 / 0.8)}.text-on-surface-variant{--tw-text-opacity:1;color:rgb(69 71 76 / var(--tw-text-opacity, 1))}.text-on-surface-variant\/70{color:rgb(69 71 76 / 0.7)}.text-outline{--tw-text-opacity:1;color:rgb(117 119 125 / var(--tw-text-opacity, 1))}.text-primary{--tw-text-opacity:1;color:rgb(9 20 38 / var(--tw-text-opacity, 1))}.text-secondary{--tw-text-opacity:1;color:rgb(80 95 118 / var(--tw-text-opacity, 1))}.underline{-webkit-text-decoration-line:underline;text-decoration-line:underline}.decoration-outline-variant{-webkit-text-decoration-color:#c5c6cd;text-decoration-color:#c5c6cd}.opacity-60{opacity:0.6}.shadow-sm{--tw-shadow:0 1px 2px 0 rgb(0 0 0 / 0.05);--tw-shadow-colored:0 1px 2px 0 var(--tw-shadow-color);box-shadow:var(--tw-ring-offset-shadow, 0 0 #0000), var(--tw-ring-shadow, 0 0 #0000), var(--tw-shadow)}.transition-all{transition-property:all;transition-timing-function:cubic-bezier(0.4, 0, 0.2, 1);transition-duration:150ms}.transition-colors{transition-property:color, background-color, border-color, fill, stroke, -webkit-text-decoration-color;transition-property:color, background-color, border-color, text-decoration-color, fill, stroke;transition-property:color, background-color, border-color, text-decoration-color, fill, stroke, -webkit-text-decoration-color;transition-timing-function:cubic-bezier(0.4, 0, 0.2, 1);transition-duration:150ms}.duration-200{transition-duration:200ms}.hover\:bg-primary-container:hover{--tw-bg-opacity:1;background-color:rgb(30 41 59 / var(--tw-bg-opacity, 1))}.hover\:bg-surface-container-low:hover{--tw-bg-opacity:1;background-color:rgb(245 243 244 / var(--tw-bg-opacity, 1))}.hover\:bg-surface-variant:hover{--tw-bg-opacity:1;background-color:rgb(228 226 227 / var(--tw-bg-opacity, 1))}.hover\:text-on-primary:hover{--tw-text-opacity:1;color:rgb(255 255 255 / var(--tw-text-opacity, 1))}.hover\:text-primary:hover{--tw-text-opacity:1;color:rgb(9 20 38 / var(--tw-text-opacity, 1))}.active\:scale-95:active{--tw-scale-x:.95;--tw-scale-y:.95;transform:translate(var(--tw-translate-x), var(--tw-translate-y)) rotate(var(--tw-rotate)) skewX(var(--tw-skew-x)) skewY(var(--tw-skew-y)) scaleX(var(--tw-scale-x)) scaleY(var(--tw-scale-y))}.group:hover .group-hover\:text-primary{--tw-text-opacity:1;color:rgb(9 20 38 / var(--tw-text-opacity, 1))}</style></head>
<body class="bg-surface overflow-x-hidden select-none" style="font-family: Inter, sans-serif; background-color: rgb(251, 248, 250); color: rgb(27, 27, 29); font-size: 12px;">
<!-- TopNavBar: Compact Navy (COMPONENTS_4 Style) -->
<header class="bg-primary text-on-primary sticky top-0 z-50 shadow-sm">
<div class="flex justify-between items-center w-full px-margin-page h-12"><div class="flex items-center space-x-6">
<span class="text-headline-md font-headline-md font-black tracking-tighter text-on-primary">TexTrack ERP</span>
<nav class="flex space-x-5 h-full items-center">
<a class="nav-link text-on-primary/80 font-medium text-label-md hover:text-on-primary transition-all cursor-pointer" href="file:///C:/Users/VICTUS/Desktop/!DOCTYPE%20.html#">Masters</a>
<a class="nav-link active text-on-primary font-bold text-label-md border-b-2 border-on-primary pb-1 cursor-pointer" href="file:///C:/Users/VICTUS/Desktop/!DOCTYPE%20.html#">Entries</a>
<a class="nav-link text-on-primary/80 font-medium text-label-md hover:text-on-primary transition-all cursor-pointer" href="file:///C:/Users/VICTUS/Desktop/!DOCTYPE%20.html#">Reports</a>
<a class="nav-link text-on-primary/80 font-medium text-label-md hover:text-on-primary transition-all cursor-pointer" href="file:///C:/Users/VICTUS/Desktop/!DOCTYPE%20.html#">Settings</a>
</nav>
</div>
<div class="flex items-center space-x-4">
<div class="flex items-center space-x-1.5">
<span class="w-1.5 h-1.5 rounded-full bg-emerald-400 pulse-indicator"></span>
<span class="text-[10px] text-on-primary/80 uppercase tracking-wider font-medium">PostgreSQL Connected</span>
</div>
<div class="h-3 w-[1px] bg-on-primary/20"></div>
<span class="text-[10px] font-bold uppercase text-on-primary">Demo Company (FY 2026-27)</span>
</div></div>
</header>
<main class="w-full px-margin-page pt-3 flex flex-col gap-3 mb-12">
<!-- Voucher Header Title -->
<div class="flex justify-between items-center border-b border-outline-variant pb-0.5">
<div class="flex items-center gap-2">
<span class="text-primary font-black uppercase tracking-widest text-[12px]">Material In</span>
<div class="bg-primary/10 px-1.5 py-0.5 rounded-sm">
<span class="text-primary font-bold text-[10px]">No. 3</span>
</div>
</div>
<div class="flex items-center gap-3 text-secondary text-[10px]">
<div class="flex items-center gap-1 font-bold">
<span class="material-symbols-outlined !text-[13px]">calendar_today</span>
<span class="">28-Jul-2026</span>
</div>
<span class="opacity-60 font-medium">Wednesday</span>
</div>
</div>
<!-- Ultra-Dense Header Section (Line-Field Style) -->
<div class="tally-border bg-surface-container-lowest p-2 shadow-sm grid grid-cols-12 gap-4">
<div class="col-span-9 grid grid-cols-2 gap-x-8 gap-y-1">
<div class="flex items-center gap-2">
<label class="text-on-surface-variant font-bold uppercase w-32 shrink-0 text-[11px]">Party A/c Name</label>
<select class="tally-input flex-1 bg-surface-container-low font-medium">
<option>TEST LEDGER (Job Worker)</option>
<option>PRIMARY VENDOR PVT LTD</option>
</select>
</div>
<div class="flex items-center gap-2">
<label class="text-on-surface-variant font-bold uppercase w-32 shrink-0 text-[11px]">JWO / Order No.</label>
<select class="tally-input flex-1 bg-surface-container-low font-medium">
<option>JWO/000054/26-27</option>
<option>JWO/000055/26-27</option>
</select>
</div>
<div class="flex items-center gap-2">
<label class="text-on-surface-variant font-bold uppercase w-32 shrink-0 text-[11px]">Consumption Godown</label>
<select class="tally-input flex-1 bg-surface-container-low font-medium">
<option>JOBBER1_WIP</option>
<option>FACTORY_FLOOR</option>
</select>
</div>
<div class="flex items-center gap-2">
<label class="text-on-surface-variant font-bold uppercase w-32 shrink-0 text-[11px]">Receiving Godown</label>
<select class="tally-input flex-1 bg-surface-container-low font-medium">
<option>MAIN GODOWN</option>
<option>QC_PENDING_STORE</option>
</select>
</div>
<div class="flex items-center gap-2">
<label class="text-on-surface-variant font-bold uppercase w-32 shrink-0 text-[11px]">Batch</label>
<input class="tally-input flex-1 bg-surface-container-low" readonly="" type="text" value="B1-2026-X" style="font-family: inherit; font-size: 13px; font-weight: 600; color: rgb(30, 41, 59); background-color: rgb(241, 245, 249); padding: 4px 8px; border: 1px solid rgb(203, 213, 225); border-radius: 4px;">
</div>
<div class="flex items-center gap-2">
<label class="text-on-surface-variant font-bold uppercase w-32 shrink-0 text-[11px]">Reference No.</label>
<input class="tally-input flex-1 bg-surface-container-low" placeholder="E.g. DC-101" type="text" value="REF-2026-001" style="font-family: inherit; font-size: 13px; font-weight: 400; color: rgb(30, 41, 59); background-color: rgb(255, 255, 255); padding: 4px 8px; border: 1px solid rgb(203, 213, 225); border-radius: 4px;">
</div>
</div>
<!-- Summary Stats -->
<div class="col-span-3 border-l border-outline-variant pl-4 flex flex-col justify-center items-end">
<span class="text-[9px] text-on-surface-variant uppercase font-black">Gross Total</span>
<span class="font-black text-primary leading-tight font-mono-data text-[24px]">7,687.50</span>
<div class="flex gap-4 mt-1 text-[9px] font-bold">
<span class="text-emerald-600">QC PASSED</span>
<span class="text-secondary">ITEMS: 02</span>
</div>
</div>
</div>
<!-- Main Items Table -->
<div class="tally-border bg-white shadow-sm overflow-hidden">
<table class="w-full text-left border-collapse table-fixed zebra-table">
<thead>
<tr class="bg-surface-container-low uppercase text-[10px]">
<th class="px-2 w-[25%] py-1.5">Item Name / Details</th>
<th class="px-2 w-[8%] text-center py-1.5">Colour</th>
<th class="px-2 w-[12%] text-center py-1.5">Size Breakdown</th>
<th class="px-2 w-[9%] text-right py-1.5">Quantity</th>
<th class="px-2 w-[6%] text-center py-1.5">UQC</th>
<th class="px-2 w-[9%] text-right py-1.5">Rate</th>
<th class="px-2 w-[11%] text-right text-primary py-1.5">P.Charge (/unit)</th>
<th class="px-2 w-[20%] text-right py-1.5">Total Amount</th>
</tr>
</thead>
<tbody class="divide-y divide-surface-container-low">
<tr class="cursor-pointer group text-[10px]"><td class="px-2 font-bold text-primary text-[11px] py-1.5">FABRIC - COTTON GREY 40S</td><td class="px-2 text-center text-secondary text-[11px] py-1.5">GREY</td><td class="px-2 text-center font-mono-data text-[11px] py-1.5">L-10, XL-5</td><td class="px-2 text-right font-mono-data text-[11px] py-1.5">10.000</td><td class="px-2 text-center text-secondary text-[11px] py-1.5">MTS</td><td class="px-2 text-right font-mono-data text-[11px] py-1.5">450.00</td><td class="px-2 text-right font-mono-data text-primary font-semibold text-[11px] py-1.5">12.50</td><td class="px-2 text-right font-mono-data font-bold text-[11px] py-1.5">4,625.00</td></tr>
<tr class="cursor-pointer group text-[10px]"><td class="px-2 font-bold text-primary text-[11px] py-1.5">FABRIC - POLYESTER BLEND</td><td class="px-2 text-center text-secondary text-[11px] py-1.5">BLUE</td><td class="px-2 text-center font-mono-data text-[11px] py-1.5">STD</td><td class="px-2 text-right font-mono-data text-[11px] py-1.5">5.000</td><td class="px-2 text-center text-secondary text-[11px] py-1.5">MTS</td><td class="px-2 text-right font-mono-data text-[11px] py-1.5">600.00</td><td class="px-2 text-right font-mono-data text-primary font-semibold text-[11px] py-1.5">12.50</td><td class="px-2 text-right font-mono-data font-bold text-[11px] py-1.5">3,062.50</td></tr>
<!-- Padding Row -->
<tr class="h-6 border-none"><td colspan="8" class=""></td></tr>
</tbody>
<tfoot class="border-t border-outline-variant bg-surface-container-low/50">
<tr class="font-black text-primary">
<td class="px-2 text-right uppercase text-[9px] py-1.5" colspan="3">Voucher Totals</td>
<td class="px-2 text-right font-mono-data text-[10px] py-1.5">15.000</td>
<td class="px-2 text-center text-[9px] py-1.5">MTS</td>
<td class=""></td>
<td class=""></td>
<td class="px-2 text-right font-mono-data text-[12px] py-1.5">₹ 7,687.50</td>
</tr>
</tfoot>
</table>
</div>
<!-- Raw Material Consumption Table -->
<div class="mt-1">
<div class="flex items-center gap-1.5 mb-1 pl-1">
<span class="material-symbols-outlined text-primary !text-[13px]">inventory_2</span>
<span class="font-black text-primary uppercase text-[9px] tracking-widest">Auto-Deducted Raw Material Consumption</span>
</div>
<div class="tally-border bg-white overflow-hidden shadow-sm">
<table class="w-full text-left border-collapse zebra-table">
<thead class="bg-surface-container-low border-b border-outline-variant">
<tr class="text-on-surface-variant uppercase font-black text-[10px]">
<th class="px-2 w-[35%] py-1.5">Component Item Name</th>
<th class="px-2 text-right py-1.5">Jobber Stock (Issued)</th>
<th class="px-2 text-right py-1.5">Prev. Consumed</th>
<th class="px-2 text-right py-1.5">Available</th>
<th class="px-2 text-right text-primary py-1.5">Consume Now</th>
<th class="px-2 text-center py-1.5">UQC</th>
</tr>
</thead>
<tbody class="divide-y divide-outline-variant/10 text-[11px]">
<tr>
<td class="px-2 font-semibold text-secondary py-1.5">YARN - 40S COMBED COTTON</td>
<td class="px-2 text-right font-mono-data py-1.5">20.000</td>
<td class="px-2 text-right font-mono-data py-1.5">5.000</td>
<td class="px-2 text-right font-mono-data py-1.5">15.000</td>
<td class="px-2 text-right font-mono-data font-bold text-primary py-1.5">10.000</td>
<td class="px-2 text-center font-medium py-1.5">KGS</td>
</tr>
<tr>
<td class="px-2 font-semibold text-secondary py-1.5">CHEMICALS - DYEING AGENT X</td>
<td class="px-2 text-right font-mono-data py-1.5">100.000</td>
<td class="px-2 text-right font-mono-data py-1.5">20.000</td>
<td class="px-2 text-right font-mono-data py-1.5">80.000</td>
<td class="px-2 text-right font-mono-data font-bold text-primary py-1.5">15.000</td>
<td class="px-2 text-center font-medium py-1.5">LTR</td>
</tr>
</tbody>
</table>
</div>
</div>
<!-- Narration & Actions -->
<div class="grid grid-cols-12 gap-6 mt-1">
<div class="col-span-8 flex items-start">
<label class="text-[9px] text-on-surface-variant font-black uppercase mt-1 shrink-0 mr-3">Narration:</label>
<textarea class="flex-grow tally-input bg-white h-10 text-body-sm py-1 leading-tight resize-none border-outline-variant" placeholder="Enter remarks...">Goods received against JWO/000054/26-27 from Jobber 1. QC passed for full quantity.</textarea>
</div>
<div class="col-span-4 flex flex-col justify-end gap-1.5 items-end">
<div class="flex gap-2">
<button class="px-4 py-1 h-[24px] border border-outline text-secondary hover:bg-surface-container-low hover:text-primary active:scale-95 transition-all duration-200 uppercase text-[10px] font-bold">
                    Quit (Esc)
                </button>
<button class="px-5 py-1 h-[24px] bg-primary text-on-primary hover:bg-primary-container active:scale-95 transition-all duration-200 uppercase text-[10px] font-bold flex items-center gap-2 shadow-sm">
<span class="material-symbols-outlined !text-[14px]">check_circle</span>
                    Accept (Alt+A)
                </button>
</div>
<span class="text-[9px] text-secondary italic">Last modified by admin at 03:45 PM</span>
</div>
</div>
</main>
<!-- Sticky Status Footer (COMPONENTS_4 Footer Style) -->
<footer class="fixed bottom-0 left-0 w-full bg-surface-container-lowest border-t border-outline-variant flex justify-between items-center px-margin-page z-50 text-[11px] h-10">
<div class="flex items-center space-x-6">
<div class="flex items-center gap-2">
<span class="text-label-bold font-label-bold text-secondary">TexTrack ERP</span>
<span class="text-outline">|</span>
<span class="text-on-surface-variant font-medium">v4.2.1-stable</span>
</div>
<div class="flex space-x-4 text-on-surface-variant/70">
<a class="hover:text-primary transition-colors underline decoration-outline-variant" href="file:///C:/Users/VICTUS/Desktop/!DOCTYPE%20.html#">Remote Sync: Active</a>
<a class="hover:text-primary transition-colors underline decoration-outline-variant" href="file:///C:/Users/VICTUS/Desktop/!DOCTYPE%20.html#">Direct Print: On</a>
</div>
</div>
<div class="flex items-center space-x-2 text-on-surface-variant h-full">
<div class="flex h-full">
<div class="flex items-center px-2 bg-surface-container-high border-x border-outline-variant/30 hover:bg-surface-variant cursor-pointer transition-colors group">
<span class="font-bold mr-1 group-hover:text-primary">F2:</span>
<span class="text-secondary">Date</span>
</div>
<div class="flex items-center px-2 bg-surface-container-high border-r border-outline-variant/30 hover:bg-surface-variant cursor-pointer transition-colors group">
<span class="font-bold mr-1 group-hover:text-primary">F11:</span>
<span class="text-secondary">Features</span>
</div>
<div class="flex items-center px-2 bg-primary text-on-primary font-bold cursor-pointer hover:bg-primary-container transition-colors">
<span class="mr-1">F12:</span>
<span class="">Config</span>
</div>
</div>
<div class="pl-2 border-l border-outline-variant ml-2 font-mono-data font-bold text-primary">
            03:45:12 PM
        </div>
</div>
</footer>
<script>
    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') {
            console.log('Quitting...');
        }
        if (e.altKey && e.key.toLowerCase() === 'a') {
            e.preventDefault();
            console.log('Accepting voucher...');
        }
    });
</script>


</body></html>
- Application type: ASP.NET Core Blazor Server using interactive server components.
- Target framework: .NET 10.
- Database: PostgreSQL through Entity Framework Core and Npgsql.
- Business access pattern: Razor page calls a scoped repository/application service directly.
- Current Material In route: `/vouchers/material-in`.
- Current service: `TexTrack.Web.Services.MaterialInRepository`.
- Current aggregate root: `Voucher` with Material In child entities.
- Current shared voucher UI: `VoucherEntryFrame`, `VoucherField`, `VoucherActionBar`, `KeyboardContextScope` and `VoucherDateInput`.
- Current application does **not** expose REST controllers. The API section below defines a future adapter contract; business logic must remain in the application service/repository.

## Exact target folder hierarchy

Project root:

`C:\Users\VICTUS\Desktop\c#\Google\TexTrackERP`

```text
TexTrackERP/
├── MODULE_2_ARCHITECT.md
├── Components/
│   ├── Keyboard/
│   │   └── KeyboardContextScope.razor                 EXISTING — do not fork
│   ├── Vouchers/
│   │   └── Shared/
│   │       ├── VoucherEntryFrame.razor                EXISTING
│   │       ├── VoucherField.razor                     EXISTING
│   │       ├── VoucherActionBar.razor                 EXISTING
│   │       ├── SearchableVoucherLookup.razor          NEW
│   │       └── SearchableVoucherLookup.razor.css      NEW
│   └── Pages/
│       └── Vouchers/
│           ├── MaterialIn.razor                       MODIFY
│           └── MaterialIn.razor.css                   MODIFY ONLY AS REQUIRED
├── Domain/
│   └── Entities.cs                                    EXISTING — normally unchanged
├── Models/
│   ├── MaterialInModels.cs                            MODIFY
│   ├── VoucherLayoutProfile.cs                       EXISTING — preserve field contract
│   └── VoucherLookupModels.cs                         NEW
├── Services/
│   ├── MaterialInRepository.cs                        MODIFY
│   ├── VoucherLookupService.cs                        NEW
│   └── MaterialInValidationService.cs                 NEW
├── Data/
│   ├── TexTrackDbContext.cs                           EXISTING — normally unchanged
│   └── Migrations/                                    NO migration unless schema truly changes
├── Api/                                               OPTIONAL FUTURE HTTP ADAPTER
│   ├── MaterialInEndpoints.cs                         NEW only if HTTP API is enabled
│   └── VoucherLookupEndpoints.cs                      NEW only if HTTP API is enabled
├── Tests/
│   ├── TexTrack.Web.IntegrationTests/
│   │   ├── MaterialInLifecycleTests.cs                NEW
│   │   └── MaterialInLookupTests.cs                   NEW
│   └── JavaScript/
│       └── material-in-keyboard.test.cjs              NEW
└── wwwroot/
    ├── css/app.css                                    DO NOT add page-specific rules here
    └── js/textrack-keyboard-contexts.js               EXISTING — do not modify unless proven necessary
```

## File-by-file responsibilities

### `Components/Vouchers/Shared/SearchableVoucherLookup.razor`

One reusable typed lookup control for Party, JWO and Godown selection. It must not contain Material In business rules.

Responsibilities:

- Render a text input, not a native HTML `select`.
- Accept a stable selected ID and visible display text as separate values.
- Accept an async search callback returning a bounded result page.
- Start searching after two typed characters, except when explicitly opened with ArrowDown or focus.
- Debounce typing by approximately 150–250 ms.
- Cancel the previous search when the text changes again.
- Display at most the requested page size; default 20 and maximum 50.
- Track a highlighted result independently from the selected result.
- ArrowDown/ArrowUp changes the highlighted result.
- Enter commits the highlighted result.
- Mouse click commits exactly the clicked result.
- Escape closes the dropdown and returns focus to the input without clearing the committed value.
- Backspace must remain governed by the existing shared voucher keyboard contract.
- When `Locked=true`, show the saved display value, set `readonly`, remove the control from normal search behavior and do not clear its value.
- When `Disabled=true`, make it completely unavailable and skipped by normal tab navigation.
- When a user edits committed text, clear the selected ID immediately so stale IDs cannot be submitted with new visible text.
- Expose `SelectedIdChanged`, `TextChanged`, `SelectionCommitted`, `Opened` and `Closed` callbacks.
- Generate deterministic element IDs so focus restoration can target the exact control.

It must never query EF Core or call `MaterialInRepository` directly.

### `Components/Vouchers/Shared/SearchableVoucherLookup.razor.css`

Responsibilities:

- Use existing global theme tokens from `wwwroot/css/app.css`.
- Keep the popup anchored directly below the input.
- Ensure popup `z-index` is above the voucher paper but below modal dialogs.
- Ensure `pointer-events: auto` on the popup and its results.
- Make highlighted, hovered and keyboard-selected rows visually identical.
- Keep text readable in normal, focused, locked and error states.
- Do not change unrelated page sizing, table styling or top navigation.

### `Models/VoucherLookupModels.cs`

Define UI-safe lookup contracts:

```csharp
public sealed record VoucherLookupQuery(
    string SearchText,
    int Page = 1,
    int PageSize = 20,
    long? ParentId = null,
    long? ExcludeVoucherId = null);

public sealed record VoucherLookupItem(
    long Id,
    string PrimaryText,
    string SecondaryText,
    string SearchText,
    bool IsSelectable = true,
    string DisabledReason = "");

public sealed record VoucherLookupPage(
    IReadOnlyList<VoucherLookupItem> Items,
    int Page,
    int PageSize,
    int TotalCount);
```

Rules:

- `SearchText` is normalized only for searching, never displayed back as normalized text.
- `PrimaryText` is the ledger/order/godown name shown prominently.
- `SecondaryText` contains group, date/batch or contextual information.
- A lookup response must never expose entity navigation objects.

### `Services/VoucherLookupService.cs`

Centralized query-only service for voucher lookup controls.

Public methods:

```csharp
Task<VoucherLookupPage> SearchJobWorkersAsync(VoucherLookupQuery query, CancellationToken ct = default);
Task<VoucherLookupPage> SearchPendingJwoAsync(long jobWorkerLedgerId, VoucherLookupQuery query, CancellationToken ct = default);
Task<VoucherLookupPage> SearchGodownsAsync(VoucherLookupQuery query, CancellationToken ct = default);
Task<VoucherLookupItem?> GetJobWorkerByIdAsync(long id, CancellationToken ct = default);
Task<VoucherLookupItem?> GetJwoByIdAsync(long id, CancellationToken ct = default);
Task<VoucherLookupItem?> GetGodownByIdAsync(long id, CancellationToken ct = default);
```

Business/query rules:

- Always filter by `CurrentCompanyContext.CompanyId`.
- JWO queries also filter by current financial year.
- Job Workers: `Ledger.IsActive && Ledger.IsJobWorker` only.
- Search Job Workers by ledger name and optionally ledger-group name using PostgreSQL `ILIKE`.
- Godowns: active current-company Godowns only.
- Search Godowns by name using `ILIKE`.
- JWO search requires a valid selected Job Worker.
- JWO must belong to that worker and must not be cancelled.
- Pending JWO calculation must exclude quantities received by active Material In vouchers.
- During alteration, exclude the current Material In voucher from prior-receipt totals so its saved JWO remains resolvable.
- Search JWO by voucher number, batch and reference.
- JWO result secondary text format: `dd-MM-yyyy · Batch {batch} · Pending {quantity}`.
- Apply server-side `OrderBy`, `Skip` and `Take`; never load all ledgers or Godowns into browser memory.
- Use `AsNoTracking()` and projection.
- Enforce `Page >= 1`, `1 <= PageSize <= 50`, and trimmed search length <= 100.

### `Services/MaterialInValidationService.cs`

One domain validation coordinator used by Save, Delete and Cancel. It receives a `TexTrackDbContext` created by the calling transaction; it must not create its own transaction or context.

Required methods:

```csharp
Task ValidateSaveAsync(TexTrackDbContext db, MaterialInSaveRequest request, MaterialInPersistedSnapshot? original, CancellationToken ct);
Task<MaterialInDeleteDecision> ValidateDeleteAsync(TexTrackDbContext db, long voucherId, CancellationToken ct);
Task ValidateCancelAsync(TexTrackDbContext db, long voucherId, string reason, CancellationToken ct);
```

It owns validation only. It must not mutate entities.

### `Models/MaterialInModels.cs`

Preserve existing models and add:

```csharp
public sealed class MaterialInPersistedSnapshot
{
    public long VoucherId { get; init; }
    public long JobWorkerLedgerId { get; init; }
    public long JwoVoucherId { get; init; }
    public string ConcurrencyToken { get; init; } = "";
    public string Status { get; init; } = "";
}

public sealed class MaterialInDeleteDecision
{
    public bool CanDelete { get; init; }
    public string Reason { get; init; } = "";
}

public sealed class MaterialInDeleteRequest
{
    public long VoucherId { get; init; }
    public string ConcurrencyToken { get; init; } = "";
}

public sealed class MaterialInCancelRequest
{
    public long VoucherId { get; init; }
    public string ConcurrencyToken { get; init; } = "";
    public string Reason { get; init; } = "";
}
```

`MaterialInEditData` must always contain `JobWorkerName`, the saved JWO display number, saved Godown names and `CanDelete`/`DeleteBlockedReason`, allowing the UI to display locked values without re-searching.

### `Services/MaterialInRepository.cs`

This remains the authoritative transactional application service.

Required public surface:

```csharp
Task<MaterialInVoucherDefaults> GetEntryDefaultsAsync(CancellationToken ct = default);
Task<IReadOnlyList<MaterialInListItem>> GetListAsync(string search = "", CancellationToken ct = default);
Task<MaterialInEditData?> GetForEditAsync(long voucherId, CancellationToken ct = default);
Task<MaterialInSaveResult> SaveAsync(MaterialInSaveRequest request, CancellationToken ct = default);
Task<OperationResult> CancelAsync(long voucherId, string reason, CancellationToken ct = default);
Task<OperationResult> DeleteAsync(MaterialInDeleteRequest request, CancellationToken ct = default);
```

Save behavior:

- Use one PostgreSQL transaction with `Serializable` isolation.
- On alteration, load the persisted voucher, detail, finished goods, allocations, consumptions, Material Out allocations and stock movements.
- Compare concurrency token before any delete or mutation.
- Party Ledger and JWO are immutable after save. Reject manipulated requests if either ID differs from the persisted snapshot.
- Voucher type and voucher number obey existing numbering rules.
- Validate all quantities before removing old child rows.
- Remove and rebuild child rows only after all preconditions pass.
- Recreate stock movements and voucher links in the same transaction.
- Commit only after the complete aggregate and audit log save successfully.
- On any exception, roll back every change.

Cancel behavior:

- Require a nonblank reason, maximum 500 characters.
- Reject an already cancelled voucher.
- Reject when an active downstream voucher link requires prior cancellation.
- Create reversing stock movements; do not delete original movements or child rows.
- Set `Voucher.Status = "Cancelled"`, cancellation reason, timestamp, user and a new concurrency token.
- Write one successful audit log.
- Complete in one serializable transaction.

Delete behavior:

- Delete is not the same as cancellation.
- Permit Delete only for an open Material In voucher that has no active downstream dependency and whose removal cannot make any affected stock key negative at the voucher date or later.
- A stock key is `(CompanyId, FinancialYearId, StockItemId, StockItemVariantId, GodownId, UqcId)`.
- Recalculate running balance for every affected key in movement-date and movement-ID order while excluding all movements belonging to the voucher being deleted.
- If any recalculated running balance becomes negative, reject deletion and instruct the user to cancel instead.
- Reject when any `VoucherLink` uses this voucher as an active source.
- Verify concurrency token immediately before mutation.
- Delete in dependency order: audit-independent links targeting the voucher, Material In-to-Material Out allocations, stock movements, finished-good allocations, consumptions, finished-good lines, Material In detail and finally Voucher.
- Write the Delete audit log in an audit mechanism that survives aggregate deletion. If current `AuditLog.EntityId` permits the deleted ID without an FK, insert it in the same transaction. Otherwise do not introduce a migration silently; report the schema blocker.
- Roll back fully on any error.

Required user-facing rejection messages:

- `The original Party A/c Name cannot be changed during Material In alteration.`
- `The original JWO / Order Number cannot be changed during Material In alteration.`
- `This Material In voucher was changed by another operation. Reopen it and try again.`
- `This Material In voucher cannot be deleted because later stock activity depends on it. Cancel the voucher instead.`
- `Cancel linked downstream vouchers first.`
- `A cancelled Material In voucher cannot be deleted. It must remain for audit.`

### `Components/Pages/Vouchers/MaterialIn.razor`

UI orchestration only; no EF queries and no stock calculations.

Create-mode field rules:

- Party A/c Name: searchable lookup; required; first normal entry field.
- JWO / Order No.: searchable lookup; required; disabled until a Party is committed.
- Consumption Godown: searchable lookup; required.
- Receiving Godown: searchable lookup; required.
- Batch: read-only from selected JWO.
- Reference: editable.
- Date: shared `VoucherDateInput`.

Alteration-mode field rules:

- Party A/c Name: visible and locked, not blank and not replaceable.
- JWO / Order No.: visible and locked, not blank and not replaceable.
- Consumption Godown: searchable and editable unless the repository later returns an explicit lock.
- Receiving Godown: searchable and editable unless explicitly locked.
- Date, Reference, Narration and allowed quantities remain editable subject to validation.
- Cancelled vouchers open read-only or remain in list view according to existing application convention; never allow Save.

Commands:

- `Alt+A`: Save/Update through existing `KeyboardContextScope`.
- `Alt+D`: open TexTrack Delete confirmation only when repository decision says deletion is permitted.
- `Alt+X`: open TexTrack Cancel dialog for an existing open voucher.
- `Esc`: existing dirty-form quit behavior.
- Never use browser `alert`, `confirm` or `prompt`.
- Modal close restores focus to the exact previously focused field or row.

The page must not duplicate the global capture listener or introduce page-level JavaScript registration.

### `Components/Pages/Vouchers/MaterialIn.razor.css`

Only Material In-specific layout corrections belong here. The file may style lookup placement inside Material In but must not redefine global palette values. Preserve the shared frame, field placement, tables and action-bar positions.

### `Tests/TexTrack.Web.IntegrationTests/MaterialInLookupTests.cs`

Required PostgreSQL tests:

- Search returns only active Job Worker ledgers in the current company.
- Search is case-insensitive and matches partial name.
- Pagination is enforced server-side.
- Godown search excludes inactive and other-company rows.
- JWO search requires and filters by selected Job Worker.
- Cancelled JWO is excluded.
- Fully received JWO is excluded in create mode.
- Current JWO remains resolvable in alteration mode when the current Material In voucher is excluded from prior totals.
- Lookup-by-ID returns the exact saved display value.

### `Tests/TexTrack.Web.IntegrationTests/MaterialInLifecycleTests.cs`

Required PostgreSQL tests:

- Create posts finished goods, consumption, allocations and stock movements atomically.
- Alteration with unchanged Party and JWO succeeds.
- Manipulated Party ID is rejected with no persisted changes.
- Manipulated JWO ID is rejected with no persisted changes.
- Stale concurrency token is rejected.
- Cancellation reverses stock and preserves original child rows.
- Cancellation requires a reason.
- Delete succeeds for an unused latest voucher.
- Delete removes all Material In children and stock movements atomically.
- Delete is blocked when a downstream voucher link exists.
- Delete is blocked when exclusion would produce a negative later running balance.
- Failed Delete leaves voucher, children, allocations, links and movements unchanged.
- Cancelled voucher cannot be deleted.

### `Tests/JavaScript/material-in-keyboard.test.cjs`

Required UI-contract tests:

- Typing filters Party results.
- Typing filters JWO results after Party selection.
- Typing filters both Godown fields.
- Arrow keys change highlighted lookup row.
- Enter commits the highlighted result once.
- Mouse commits the clicked result once.
- Escape closes the lookup without leaving the voucher.
- Locked Party and JWO are skipped by normal keyboard navigation during alteration.
- Alt+D routes only to the active Material In alteration form.
- Delete dialog owns keyboard commands while open.
- Closing Delete or Cancel restores focus.
- Lookup initialization does not register duplicate global listeners.

## Optional HTTP API contract

Do not add these endpoints unless the project is intentionally enabling HTTP APIs. If enabled, add minimal APIs in `Api/` and register them from `Program.cs`. Endpoints must call the same services used by Blazor and contain no duplicated business rules.

### Lookup endpoints

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/v1/voucher-lookups/job-workers?search=&page=1&pageSize=20` | Search active Job Workers |
| GET | `/api/v1/voucher-lookups/godowns?search=&page=1&pageSize=20` | Search active Godowns |
| GET | `/api/v1/voucher-lookups/jwo?jobWorkerId={id}&search=&page=1&pageSize=20&excludeMaterialInVoucherId={id?}` | Search eligible JWO |
| GET | `/api/v1/voucher-lookups/job-workers/{id}` | Resolve saved Job Worker display value |
| GET | `/api/v1/voucher-lookups/godowns/{id}` | Resolve saved Godown display value |
| GET | `/api/v1/voucher-lookups/jwo/{id}` | Resolve saved JWO display value |

### Material In endpoints

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/v1/material-in` | Paged Material In list |
| GET | `/api/v1/material-in/defaults` | Entry defaults |
| GET | `/api/v1/material-in/{voucherId}` | Edit/view model |
| POST | `/api/v1/material-in` | Create voucher |
| PUT | `/api/v1/material-in/{voucherId}` | Alter voucher; body ID must match route |
| POST | `/api/v1/material-in/{voucherId}/cancel` | Cancel with reason |
| DELETE | `/api/v1/material-in/{voucherId}` | Conditional hard delete with concurrency token |

HTTP response rules:

- `200` successful query/update/cancel/delete.
- `201` successful create with `Location` header.
- `400` malformed input.
- `404` wrong company/year or missing voucher.
- `409` concurrency, immutable-field, dependency or stock-balance conflict.
- Never return raw PostgreSQL or EF exception text.
- Never accept CompanyId, FinancialYearId, CreatedBy or ModifiedBy from the client.

## Complete validation gate order

Every write must run gates in this order inside one serializable transaction:

1. Resolve current company and financial year from server context.
2. Load the current persisted voucher for alteration/cancel/delete.
3. Confirm voucher belongs to current company and year.
4. Confirm status permits the requested command.
5. Confirm concurrency token for alteration/delete.
6. Confirm immutable Party and JWO IDs are unchanged on alteration.
7. Confirm voucher date lies inside the active financial year.
8. Confirm voucher type is Material In or a valid child type.
9. Confirm Party is an active Job Worker.
10. Confirm JWO belongs to that Party, company and year and is not cancelled.
11. Confirm both Godowns are active and belong to the company.
12. Confirm finished-good and component rows belong to that JWO.
13. Reject duplicate finished-good, component or variant inputs.
14. Confirm all quantities and process charges are nonnegative.
15. Confirm variant total equals finished-good received quantity.
16. Confirm receipt does not exceed pending JWO quantity after excluding current voucher during alteration.
17. Confirm consumption does not exceed unconsumed active Material Out allocations.
18. For Delete, confirm no active downstream links.
19. For Delete, simulate affected running stock balances excluding the voucher.
20. Only after every gate passes, mutate the aggregate.
21. Save audit data.
22. Commit.

Any failure before commit must leave every Voucher field, Material In child, Material Out allocation, VoucherLink, StockMovement and AuditLog exactly as it was.

## Performance and scaling requirements

- Never preload thousands of ledgers or Godowns into the component.
- All lookup search and paging must be database-side.
- Add no index migration without first inspecting existing indexes and migration order.
- If missing, separately propose indexes on normalized/search columns and JWO ownership/status columns; do not silently add them in the UI patch.
- Use cancellation tokens throughout async lookup calls.
- Debounce client search and discard stale responses.
- Use `AsNoTracking`, projection and bounded result sets.

## Definition of done

- A user can type a few letters to find Party, JWO and both Godowns among thousands of records.
- Mouse and keyboard selection behave identically.
- Alteration displays and locks saved Party/JWO values.
- Save rejects manipulated immutable IDs server-side.
- Alt+D uses TexTrack confirmation and safely deletes only eligible vouchers.
- Alt+X reverses stock and preserves the cancelled voucher for audit.
- Failed commands roll back completely.
- Existing shared Esc, Alt+A, Alt+D, Alt+X and Backspace contracts remain intact.
- Full solution restores and builds with zero errors.
- All existing tests and every new test listed above pass.
- No unrelated voucher, report, process-cost, migration or visual behavior changes.

## Required implementation report from the next AI

The implementing AI must report:

- Root cause of each confirmed defect.
- Every created and changed file using absolute paths.
- Migration assessment and whether a migration was intentionally avoided.
- Exact validation and transaction behavior for Save, Cancel and Delete.
- Restore result.
- Build result.
- JavaScript test result.
- PostgreSQL integration test result.
- Any browser/manual verification not completed.
- Remaining limitations without claiming unexecuted tests passed.
