import { useState, useRef, useEffect, useCallback } from 'react';
import {
  X,
  Minus,
  Send,
  Bot,
  Sparkles,
  ChevronDown,
} from 'lucide-react';
import { useAiAssistantStore, AiAssistantMessage } from '@stores/useAiAssistantStore';

export function CanvasAssistant() {
  const {
    messages,
    isOpen,
    isCollapsed,
    isTyping,
    addMessage,
    clearChat,
    toggleOpen,
    toggleCollapsed,
    generateResponse,
  } = useAiAssistantStore();

  const [input, setInput] = useState('');
  const messagesEndRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  const scrollToBottom = useCallback(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, []);

  useEffect(() => {
    scrollToBottom();
  }, [messages, scrollToBottom]);

  const handleSubmit = useCallback(
    async (e: React.FormEvent) => {
      e.preventDefault();
      if (!input.trim()) return;

      const userText = input.trim();
      setInput('');
      addMessage('user', userText);

      const response = await generateResponse(userText);
      addMessage('assistant', response);
    },
    [input, addMessage, generateResponse]
  );

  if (!isOpen) {
    return (
      <button
        onClick={toggleOpen}
        className="absolute top-3 left-1/2 -translate-x-1/2 z-[10] flex items-center gap-2 px-4 py-2 rounded-full bg-gray-900/90 border border-gray-700/50 text-sm text-gray-300 hover:text-white hover:border-indigo-500/50 transition-all shadow-lg backdrop-blur-sm"
        title="Open AI Assistant"
      >
        <Bot className="w-4 h-4 text-indigo-400" />
        <span>AI Assistant</span>
        <Sparkles className="w-3 h-3 text-amber-400" />
      </button>
    );
  }

  return (
    <div
      className={`absolute top-3 left-1/2 -translate-x-1/2 z-[10] transition-all duration-300 ${
        isCollapsed ? 'w-72' : 'w-[480px]'
      }`}
    >
      <div className="bg-gray-900/95 backdrop-blur-sm border border-gray-700/50 rounded-xl shadow-2xl overflow-hidden">
        {/* Header */}
        <div className="flex items-center justify-between px-4 py-2.5 border-b border-gray-700/50">
          <div className="flex items-center gap-2">
            <div className="w-6 h-6 rounded-md bg-indigo-600/20 flex items-center justify-center">
              <Bot className="w-3.5 h-3.5 text-indigo-400" />
            </div>
            <span className="text-sm font-medium text-gray-200">
              AI Assistant
            </span>
            <Sparkles className="w-3 h-3 text-amber-400" />
          </div>
          <div className="flex items-center gap-1">
            <button
              onClick={toggleCollapsed}
              className="p-1 rounded-md hover:bg-gray-700/50 text-gray-400 hover:text-gray-200 transition-colors"
              title={isCollapsed ? 'Expand' : 'Collapse'}
            >
              {isCollapsed ? (
                <ChevronDown className="w-3.5 h-3.5" />
              ) : (
                <Minus className="w-3.5 h-3.5" />
              )}
            </button>
            <button
              onClick={toggleOpen}
              className="p-1 rounded-md hover:bg-gray-700/50 text-gray-400 hover:text-gray-200 transition-colors"
              title="Close Assistant"
            >
              <X className="w-3.5 h-3.5" />
            </button>
          </div>
        </div>

        {!isCollapsed && (
          <>
            {/* Messages */}
            <div className="h-64 overflow-y-auto px-4 py-3 space-y-3">
              {messages.map((msg) => (
                <MessageBubble key={msg.id} message={msg} />
              ))}
              {isTyping && (
                <div className="flex items-start gap-2">
                  <div className="w-6 h-6 rounded-full bg-indigo-600/20 flex items-center justify-center flex-shrink-0">
                    <Bot className="w-3 h-3 text-indigo-400" />
                  </div>
                  <div className="flex items-center gap-1 px-3 py-2 rounded-xl bg-gray-800/50">
                    <div className="w-1.5 h-1.5 rounded-full bg-gray-500 animate-bounce" style={{ animationDelay: '0s' }} />
                    <div className="w-1.5 h-1.5 rounded-full bg-gray-500 animate-bounce" style={{ animationDelay: '0.15s' }} />
                    <div className="w-1.5 h-1.5 rounded-full bg-gray-500 animate-bounce" style={{ animationDelay: '0.3s' }} />
                  </div>
                </div>
              )}
              <div ref={messagesEndRef} />
            </div>

            {/* Input */}
            <form
              onSubmit={handleSubmit}
              className="flex items-center gap-2 px-4 py-3 border-t border-gray-700/50"
            >
              <input
                ref={inputRef}
                type="text"
                value={input}
                onChange={(e) => setInput(e.target.value)}
                placeholder="Ask for help with your workflow..."
                className="flex-1 bg-gray-800/50 border border-gray-700/50 rounded-lg px-3 py-2 text-sm text-gray-200 placeholder-gray-500 focus:outline-none focus:border-indigo-500/50 transition-colors"
              />
              <button
                type="submit"
                disabled={!input.trim()}
                className="p-2 rounded-lg bg-indigo-600/20 text-indigo-400 hover:bg-indigo-600/30 disabled:opacity-30 disabled:cursor-not-allowed transition-colors"
              >
                <Send className="w-4 h-4" />
              </button>
              <button
                type="button"
                onClick={clearChat}
                className="p-2 rounded-lg hover:bg-gray-700/50 text-gray-400 hover:text-gray-200 transition-colors"
                title="Clear chat"
              >
                <X className="w-4 h-4" />
              </button>
            </form>
          </>
        )}
      </div>
    </div>
  );
}

function MessageBubble({ message }: { message: AiAssistantMessage }) {
  const isUser = message.role === 'user';
  const isSystem = message.role === 'system';

  if (isSystem) {
    return (
      <div className="text-center text-xs text-gray-500 py-1">
        {message.content}
      </div>
    );
  }

  return (
    <div className={`flex items-start gap-2 ${isUser ? 'flex-row-reverse' : ''}`}>
      {!isUser && (
        <div className="w-6 h-6 rounded-full bg-indigo-600/20 flex items-center justify-center flex-shrink-0 mt-0.5">
          <Bot className="w-3 h-3 text-indigo-400" />
        </div>
      )}
      <div
        className={`max-w-[80%] rounded-xl px-3 py-2 text-sm leading-relaxed ${
          isUser
            ? 'bg-indigo-600/20 text-indigo-100'
            : 'bg-gray-800/50 text-gray-300'
        }`}
      >
        <MarkdownLite text={message.content} />
      </div>
    </div>
  );
}

// Lightweight markdown rendering for chat messages
function MarkdownLite({ text }: { text: string }) {
  const lines = text.split('\n');
  return (
    <>
      {lines.map((line, i) => {
        // Bold: **text**
        let processed: React.ReactNode = line;
        const boldParts = line.split(/(\*\*.*?\*\*)/g);
        if (boldParts.length > 1) {
          processed = boldParts.map((part, j) => {
            if (part.startsWith('**') && part.endsWith('**')) {
              return (
                <strong key={j} className="font-semibold text-white">
                  {part.slice(2, -2)}
                </strong>
              );
            }
            return part;
          });
        }

        // Italic: *text* (not bold)
        const italicParts = (typeof processed === 'string' ? processed : line).split(/(\*.*?\*)/g);
        if (italicParts.length > 1 && !line.includes('**')) {
          return (
            <div key={i}>
              {italicParts.map((part, j) => {
                if (part.startsWith('*') && part.endsWith('*')) {
                  return (
                    <em key={j} className="italic">
                      {part.slice(1, -1)}
                    </em>
                  );
                }
                return part;
              })}
            </div>
          );
        }

        // Bullet points
        if (line.trim().startsWith('- ') || line.trim().startsWith('* ')) {
          return (
            <div key={i} className="flex items-start gap-2 ml-2">
              <span className="text-gray-500 mt-0.5">•</span>
              <span>{processed}</span>
            </div>
          );
        }

        // Empty line
        if (line.trim() === '') {
          return <div key={i} className="h-1" />;
        }

        return <div key={i}>{processed}</div>;
      })}
    </>
  );
}
