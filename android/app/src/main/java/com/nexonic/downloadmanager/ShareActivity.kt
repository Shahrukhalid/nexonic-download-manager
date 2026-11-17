package com.nexonic.downloadmanager

import android.content.Intent
import android.os.Bundle
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import com.nexonic.downloadmanager.core.AppLanguage
import com.nexonic.downloadmanager.core.Links
import com.nexonic.downloadmanager.core.Playlists
import com.nexonic.downloadmanager.data.Downloads
import com.nexonic.downloadmanager.ui.AddDownloadSheet
import com.nexonic.downloadmanager.ui.NexonicTheme

/**
 * Links shared from other apps (Share → Download with Nexonic): the download window opens as a pop-up over the app you
 * shared from, and you stay there once the download is added. Torrents, playlists and profiles, and everyone who turned
 * the pop-up off in Settings, get the full app instead.
 */
class ShareActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        AppLanguage.load(this)
        Downloads.init(this)
        val text = listOfNotNull(intent.getStringExtra(Intent.EXTRA_TEXT), intent.getStringExtra(Intent.EXTRA_SUBJECT)).joinToString(" ")
        val links = Links.split(text)
        val single = links.singleOrNull()
        val fullApp = !Downloads.settings.sharePopup || links.isEmpty() || text.contains("magnet:?", ignoreCase = true) ||
            (single != null && Playlists.isPlaylist(single))
        if (fullApp) {
            startActivity(Intent(intent).setClass(this, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
            finish()
            return
        }
        setContent {
            NexonicTheme {
                AddDownloadSheet(given = null, text = links.joinToString("\n")) { message ->
                    if (message != null) Toast.makeText(this, message, Toast.LENGTH_SHORT).show()
                    finish()
                }
            }
        }
    }

    override fun finish() {
        super.finish()
        @Suppress("DEPRECATION") overridePendingTransition(0, 0)
    }
}
