<?php

namespace TruFraudBot;

/**
 * Handles chat with Gemini using the free Gemini API.
 * Chat uses primary model with fallbacks on quota/error.
 */
final class GeminiService
{
    /** Try highest-RPD model first to preserve stricter quotas. */
    private const CHAT_MODELS = ['gemini-2.0-flash-lite', 'gemini-2.0-flash', 'gemini-2.5-flash'];

    private const MAX_HISTORY_TURNS = 20;

    private string $apiKey;
    /** @var list<array{role: string, parts: list<array{text: string}>}> */
    private array $chatHistory = [];

    public function __construct(string $apiKey)
    {
        $this->apiKey = $apiKey;
    }

    private static function isQuotaOrNotFound(\Throwable $ex): bool
    {
        $msg = strtolower($ex->getMessage());
        return str_contains($msg, '429')
            || str_contains($msg, 'quota')
            || str_contains($msg, 'quota exceeded')
            || str_contains($msg, '404')
            || str_contains($msg, 'not found');
    }

    /**
     * Send a message and get a text response, keeping conversation history.
     * Tries each chat model in order; on quota/404 tries next, otherwise returns error message.
     */
    public function chat(string $userMessage): string
    {
        $this->chatHistory[] = [
            'role' => 'user',
            'parts' => [['text' => $userMessage]],
        ];

        $payload = [
            'contents' => $this->trimHistoryForPayload(),
            'generationConfig' => [
                'temperature' => 0.7,
                'maxOutputTokens' => 2048,
            ],
        ];

        foreach (self::CHAT_MODELS as $model) {
            try {
                $text = $this->generateContent($model, $payload);
                if ($text !== null) {
                    $this->chatHistory[] = [
                        'role' => 'model',
                        'parts' => [['text' => $text]],
                    ];
                    return $text;
                }
            } catch (\Throwable $ex) {
                $msg = $ex->getMessage();
                if (stripos($msg, 'API key') !== false || str_contains($msg, '401')) {
                    return 'Invalid or missing Gemini API key. Set GEMINI_API_KEY in .env or get a key at https://aistudio.google.com/apikey';
                }
                if (!self::isQuotaOrNotFound($ex)) {
                    return 'Gemini API error: ' . (strlen($msg) > 200 ? substr($msg, 0, 200) . '…' : $msg);
                }
            }
        }

        return 'All free-tier models are out of quota for today. Resets at midnight Pacific. See https://ai.google.dev/gemini-api/docs/rate-limits';
    }

    public function clearHistory(): void
    {
        $this->chatHistory = [];
    }

    public function hasHistory(): bool
    {
        return $this->chatHistory !== [];
    }

    /**
     * Last N turns (user+model pairs) to keep payload size and TPM under control.
     * @return list<array{role: string, parts: list<array{text: string}>}>
     */
    private function trimHistoryForPayload(): array
    {
        $maxEntries = self::MAX_HISTORY_TURNS * 2;
        if (count($this->chatHistory) <= $maxEntries) {
            return $this->chatHistory;
        }
        return array_values(array_slice($this->chatHistory, -$maxEntries));
    }

    /**
     * @param array{contents: array, generationConfig: array} $payload
     */
    private function generateContent(string $model, array $payload): ?string
    {
        $url = 'https://generativelanguage.googleapis.com/v1beta/models/' . $model . ':generateContent';
        $json = json_encode($payload, JSON_THROW_ON_ERROR);

        $ch = curl_init($url);
        if ($ch === false) {
            throw new \RuntimeException('curl_init failed');
        }
        curl_setopt_array($ch, [
            CURLOPT_RETURNTRANSFER => true,
            CURLOPT_POST => true,
            CURLOPT_HTTPHEADER => [
                'Content-Type: application/json',
                'x-goog-api-key: ' . $this->apiKey,
            ],
            CURLOPT_POSTFIELDS => $json,
            CURLOPT_TIMEOUT => 120,
        ]);

        $response = curl_exec($ch);
        $errno = curl_errno($ch);
        $httpCode = (int) curl_getinfo($ch, CURLINFO_HTTP_CODE);
        curl_close($ch);

        if ($errno !== 0) {
            throw new \RuntimeException('cURL error: ' . ($response ?: 'Unknown'));
        }

        if ($httpCode >= 400) {
            $data = is_string($response) ? json_decode($response, true) : null;
            $msg = $data['error']['message'] ?? $response ?? 'HTTP ' . $httpCode;
            throw new \RuntimeException(is_string($msg) ? $msg : json_encode($msg));
        }

        $data = json_decode($response, true);
        if (!is_array($data)) {
            throw new \RuntimeException('Invalid JSON response');
        }

        $text = $data['candidates'][0]['content']['parts'][0]['text'] ?? null;
        return $text !== null ? trim($text) : '(No response.)';
    }
}
