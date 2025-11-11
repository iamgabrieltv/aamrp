set output to ""
tell application "Music"
    set t_name to name of current track
    set t_artist to artist of current track
    set t_album to album of current track
    set t_state to player state
    set output to "" & "
" & t_name & "
" & t_artist & "
" & t_album & "
" & t_state & "
"
end tell
return output