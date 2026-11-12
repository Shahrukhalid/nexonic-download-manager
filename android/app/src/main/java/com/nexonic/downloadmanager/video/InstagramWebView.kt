package com.nexonic.downloadmanager.video

import android.annotation.SuppressLint
import android.content.Context
import android.view.View
import android.webkit.WebView
import android.webkit.WebViewClient
import com.nexonic.downloadmanager.core.BatchEntry
import com.nexonic.downloadmanager.core.InstagramPage
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withContext
import kotlin.coroutines.resume

/**
 * Profile Batch for Instagram: the profile page opens in a hidden web view, like a visitor's browser (Nexonic never signs
 * in), and the video links the page shows are read with [InstagramPage.SCRIPT].
 */
object InstagramWebView {
    @SuppressLint("SetJavaScriptEnabled")
    suspend fun read(context: Context, user: String, stopped: () -> Boolean): Pair<Int, List<BatchEntry>> = withContext(Dispatchers.Main) {
        val view = WebView(context.applicationContext)
        view.settings.javaScriptEnabled = true
        view.settings.domStorageEnabled = true
        view.webViewClient = WebViewClient()
        // Laid out at a phone's size without being shown, so the page builds its grid as it would on screen.
        view.measure(View.MeasureSpec.makeMeasureSpec(1080, View.MeasureSpec.EXACTLY), View.MeasureSpec.makeMeasureSpec(1920, View.MeasureSpec.EXACTLY))
        view.layout(0, 0, 1080, 1920)
        try {
            view.loadUrl("https://www.instagram.com/$user/")
            var best: Pair<Int, List<BatchEntry>> = 0 to emptyList()
            var same = 0
            for (i in 0 until 25) {
                if (same >= 2 || stopped()) break
                delay(1000)
                val json = suspendCancellableCoroutine<String?> { c -> view.evaluateJavascript(InstagramPage.SCRIPT) { if (c.isActive) c.resume(it) } }
                val now = InstagramPage.read(json)
                same = if (now.first > 0 && now.first == best.first && now.second.size == best.second.size) same + 1 else 0
                if (now.first >= best.first) best = now
            }
            best
        } finally {
            view.stopLoading()
            view.destroy()
        }
    }
}
