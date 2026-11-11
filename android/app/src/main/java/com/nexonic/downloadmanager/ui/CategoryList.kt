package com.nexonic.downloadmanager.ui

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Add
import androidx.compose.material.icons.outlined.ArrowDownward
import androidx.compose.material.icons.outlined.ArrowUpward
import androidx.compose.material.icons.outlined.Delete
import androidx.compose.material3.Card
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.nexonic.downloadmanager.core.CustomCategory
import com.nexonic.downloadmanager.core.tr
import com.nexonic.downloadmanager.data.Downloads

/**
 * Settings → Downloads: the user's own categories as cards (icon, name, file types, sites, folder) that can be added,
 * moved and removed, or edited as text. Both are checked by the same parser, so they always agree.
 */
@Composable
fun CategoryList() {
    val s = Downloads.settings
    val rows = remember { mutableStateListOf<CustomCategory>().apply { addAll(s.customCategories) } }
    var asText by remember { mutableStateOf(false) }
    var text by remember { mutableStateOf("") }
    var problem by remember { mutableStateOf<String?>(null) }
    var saved by remember { mutableStateOf(false) }

    Row(verticalAlignment = Alignment.CenterVertically) {
        Text(tr("Your categories"), style = MaterialTheme.typography.bodyLarge, modifier = Modifier.weight(1f))
        TextButton(onClick = {
            if (!asText) { text = CustomCategory.toLines(rows); asText = true }
            else {
                val (list, p) = CustomCategory.parse(text)
                problem = p
                if (p == null) { rows.clear(); rows.addAll(list); asText = false }
            }
        }) { Text(tr(if (asText) "Edit as list" else "Edit as text")) }
    }
    if (asText) {
        OutlinedTextField(text, { text = it; problem = null; saved = false }, Modifier.fillMaxWidth(), minLines = 3,
            label = { Text(tr("One per line: [icon] Name: types @ sites > folder")) })
    } else {
        if (rows.isEmpty()) Hint("No categories yet. Add one to sort files of your own types into their own folder.")
        rows.forEachIndexed { i, c ->
            Card(Modifier.fillMaxWidth().padding(vertical = 4.dp)) {
                Column(Modifier.padding(8.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        OutlinedTextField(c.icon, { rows[i] = c.copy(icon = it); saved = false }, Modifier.width(72.dp), singleLine = true, label = { Text(tr("Icon")) })
                        OutlinedTextField(c.name, { rows[i] = c.copy(name = it); saved = false }, Modifier.weight(1f).padding(start = 6.dp), singleLine = true, label = { Text(tr("Name")) })
                    }
                    OutlinedTextField(c.extensions, { rows[i] = c.copy(extensions = it); saved = false }, Modifier.fillMaxWidth(), singleLine = true,
                        label = { Text(tr("Types (cbz cbr)")) })
                    OutlinedTextField(c.sites, { rows[i] = c.copy(sites = it); saved = false }, Modifier.fillMaxWidth(), singleLine = true,
                        label = { Text(tr("Sites (optional)")) })
                    OutlinedTextField(c.folder, { rows[i] = c.copy(folder = it); saved = false }, Modifier.fillMaxWidth(), singleLine = true,
                        label = { Text(tr("Folder inside Downloads/Nexonic")) })
                    Row {
                        IconButton(enabled = i > 0, onClick = { rows.add(i - 1, rows.removeAt(i)); saved = false }) { Icon(Icons.Outlined.ArrowUpward, tr("Move up")) }
                        IconButton(enabled = i < rows.size - 1, onClick = { rows.add(i + 1, rows.removeAt(i)); saved = false }) { Icon(Icons.Outlined.ArrowDownward, tr("Move down")) }
                        IconButton(onClick = { rows.removeAt(i); saved = false }) { Icon(Icons.Outlined.Delete, tr("Remove")) }
                    }
                }
            }
        }
        TextButton(onClick = { rows.add(CustomCategory("")); saved = false }) {
            Icon(Icons.Outlined.Add, null); Text(tr("Add Category"))
        }
        Hint("Checked in this order before the built-in types. Sites limit a category to files from those addresses (example.com includes its subdomains; * matches anything).")
    }
    problem?.let { Text(it, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodySmall) }
    Row(verticalAlignment = Alignment.CenterVertically) {
        TextButton(onClick = {
            val (list, p) = CustomCategory.parse(if (asText) text else CustomCategory.toLines(rows.filter { it.name.isNotBlank() || it.extensions.isNotBlank() }))
            problem = p
            if (p == null) { s.customCategories = list; rows.clear(); rows.addAll(list); text = CustomCategory.toLines(list); saved = true }
        }) { Text(tr("Save Categories")) }
        if (saved) Text(tr("Saved"), style = MaterialTheme.typography.bodySmall)
    }
}
