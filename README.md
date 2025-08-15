## Playnite F95Zone Metadata Plugin

Since the original plugin is no longer being maintained, I decided to fork it.

**You MUST go into plugin settings and use the login button. I have removed manual cookie insertion due to various problems/inefficiency. You shouldn't need to manually input your cookies anyway.**

## About
This Playnite plugin is used to fetch game metadata from F95Zone.

Currently supports:
- Title
- Features (engine, game status, etc)
- Genre
- Tags
- Version
- Links
- Description
- Rating
- Author
- Publisher
- Cover Image
- Background Image
- Icon (f95zone, static) 

## How to install:
1. Download the latest release, it should be a .pext file. 
2. Drag the downloaded .pext file into your Playnite window. 
3. Click the "Yes" button. 
4. **Steps 5+ are required for searching and full metadata scraping.**
5. Click on the Playnite logo in the top-left. 
6. Click on "Add-ons". 
7. Click on Extension Settings > Metadata Sources > F95Zone. 
8. Click on the Login button and log in to your F95Zone account. 
9. If you want to automatically check your games for updates, make sure to enable it while you're in the plugin settings. 
10. Once complete, you can now click the Save button. Your cookies are automatically saved on login regardless. 

Now, whenever you add a game and click the "Download Metadata..." button, an F95Zone button should appear. If a page is protected by DDoS-Guard, you will see a brief window open and close while it scrapes the metadata. 

## FAQ/Troubleshooting
### Why does it give me an error when I try to scrape data?
Try logging in again (steps 7-8). Otherwise, open an issue and attach your most recent log dump. 

### Why does it give me a "forbidden" error?
Use a VPN or wait a few hours. This is tied to your IP address and it is separate from ddos-guard. 

### Does this still work if they disable DDoS-Guard in the future?
Yes.

### Why do I log in and nothing happens/I immediately get logged out? 
This is a cookie synchronization issue. Delete your plugin settings file and try again.

### How aggressive is this? Will my account get banned?
Unless you enable the update checker and have a massive collection of games (50+), my modifications are very non-aggressive. I cannot speak on the safety for your account, however it is incredibly unlikely your account would be banned unless you are sending thousands of requests to their servers per minute. Even if you have, for example, 200 games in your library, I highly doubt it would be enough traffic for them to care. Just try to be respectful and appreciate the free service they provide for you.

### Do you store my login details?
Your cookies are stored locally in the plugin's settings file. They are never sent anywhere or saved to a remote server.

### The bypass is too slow! Can I make it faster?
No. DDoS-Guard uses something called a "JS challenge" to detect bots. This requires a full web browser to execute the code required for the challenge. If you want to avoid this, try to prevent DDoS-Guard from triggering in the first place through various means.

## Contribution
You can contribute in a number of ways. If you want to report a problem or suggest a feature, make an issue.
If you'd like to make a change, feel free to open a pull request.
