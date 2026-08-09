 ┌────────────────────────┬─────────────────────────────────────────────────────────────────────┐
 │ Feature                │ Details                                                             │
 ├────────────────────────┼─────────────────────────────────────────────────────────────────────┤
 │ Full stack start       │ . ./start.ps1 — starts .NET backend (:5000) + Vite frontend (:3001) │
 ├────────────────────────┼─────────────────────────────────────────────────────────────────────┤
 │ Frontend only          │ ./start.ps1 --frontend — Vite dev server only                       │
 ├────────────────────────┼─────────────────────────────────────────────────────────────────────┤
 │ Backend only           │ ./start.ps1 --backend — .NET API only                               │
 ├────────────────────────┼─────────────────────────────────────────────────────────────────────┤
 │ Production build       │ ./start.ps1 --build — npm run build then .NET serves dist/          │
 ├────────────────────────┼─────────────────────────────────────────────────────────────────────┤
 │ Prerequisites check    │ Validates Node.js and .NET SDK before starting                      │
 ├────────────────────────┼─────────────────────────────────────────────────────────────────────┤
 │ Auto npm install       │ Installs dependencies if node_modules missing                       │
 ├────────────────────────┼─────────────────────────────────────────────────────────────────────┤
 │ Port conflict handling │ Detects and kills existing processes on ports 5000/3001             │
 ├────────────────────────┼─────────────────────────────────────────────────────────────────────┤
 │ Health checks          │ Waits for both services to respond before declaring ready           │
 ├────────────────────────┼─────────────────────────────────────────────────────────────────────┤
 │ Auto browser open      │ Opens the correct URL in the default browser                        │
 ├────────────────────────┼─────────────────────────────────────────────────────────────────────┤
 │ Graceful shutdown      │ Ctrl+C kills all child processes cleanly                            │
 ├────────────────────────┼─────────────────────────────────────────────────────────────────────┤
 │ Colored output         │ ANSI colors for status, errors, and info messages                   │
 └────────────────────────┴─────────────────────────────────────────────────────────────────────┘
