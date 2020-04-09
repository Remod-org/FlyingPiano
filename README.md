# FlyingPiano for Rust (Remod original)

## Overview
**Flying Piano for Rust** is an Oxide plugin which allows an enabled user to spawn and ride their own flying piano.  The piano consists of a piano, code lock, and lantern.  The lantern is used to take off and land.

There are two modes of operation depending on the permission granted to the user.  The default mode requires low-grade fuel in the lantern in order to fly.  The unlimited mode does not require fuel.

For the default mode, the user will receive notification via chat message as well as an audible water pump sound when fuel is low (1 low grade fuel).  Each unit of low grade fuel gives you 10 minutes of flying time, which is the same rate of usage as the standard lantern.  When you run out of fuel, the piano will land itself immediately.

![](https://i.imgur.com/vgd64oR.jpg)

## Permissions

* flyingpiano.use -- Allows player to spawn and fly a piano using low grade fuel
* flyingpiano.unlimited -- Removes the fuel requirement

It is suggested that you create groups for each mode:
* oxide.group add fp
* oxide.group add fpunlimited

Then, add the associated permissions to each group:
* oxide.grant group fp flyingpiano.use
* oxide.grant group fpunlimited flyingpiano.unlimited

Finally, add users to each group as desired:
* oxide.usergroup add rfc1920 fp

Of course, you could grant, for example, unlimited use to all players:
* oxide.grant group default fp.unlimited

## Chat Commands

* /fp  -- Spawn a flying piano
* /fpd -- Despawn a flying piano (must be within 10 meters of the piano)
* /fpc -- List the current number of pianos (Only useful if limit set higher than 1 per user)
* /fphelp -- List the available commands (above)

## Configuration
Configuration is done via the FlyingPiano.json file under the oxide/config directory.  Following is the default:
```json
{
    "Deploy - Enable limited FlyingPianos per person : ": true,
    "Deploy - Limit of pianos players can build : ": 1,
    "Minimum Distance for FPD: ": 10.0,
    "Minimum Flight Altitude : ": 2.0,
    "Require Fuel to Operate : ": true,
    "Speed - Normal Flight Speed is : ": 12.0,
    "Speed - Sprint Flight Speed is : ": 25.0
}
```
Note that that owner/admin can customize global fuel requirements and flying speed, and limit the number of pianos for each player (highly recommended).

You *could* set "Require Fuel to Operate : " to false, but it is recommended that you leave this setting true and use the flyingpiano.unlimited permission instead if you want to remove the fuel requirement.

## Flight School
1. Type /fp to spawn a piano.
2. Set a code on the lock.  Unlock after setting the code.
2. Add low-grade fuel to the lantern (if running in default mode).
3. Sit in the piano.
4. Aim at the lantern and press 'E' to take off!
5. From here on use, WASD, Shift (sprint), spacebar (up), and Ctrl (down) to fly.
6. When ready to land, point at the lantern and press E again.
7. Once on the ground, use the spacebar to dismount.
8. Lock the piano using the code lock to prevent others from using it.
9. Use /fpd while standing next to the piano to destroy it.
## Localization
English/default language:
```json
{
  "helptext1": "Flying Piano instructions:",
  "helptext2": "  type /fp to spawn a Flying Piano",
  "helptext3": "  type /fpd to destroy your flyingpiano.",
  "helptext4": "  type /fpc to show a count of your pianos",
  "notauthorized": "You don't have permission to do that !!",
  "notfound": "Could not locate a piano.  You must be within {0} meters for this!!",
  "notflyingpiano": "You are not piloting a flying piano !!",
  "maxpianos": "You have reached the maximum allowed pianos",
  "landingpiano": "Piano landing sequence started !!",
  "risingpiano": "Piano takeoff sequence started !!",
  "pianolocked": "You must unlock the Piano first !!",
  "pianospawned": "Flying Piano spawned!  Don't forget to lock it !!",
  "pianodestroyed": "Flying Piano destroyed !!",
  "pianofuel": "You will need fuel to fly.  Do not start without fuel !!",
  "pianonofuel": "You have been granted unlimited fly time, no fuel required !!",
  "nofuel": "You're out of fuel !!",
  "noplayer": "Unable to find player {0}!",
  "gaveplayer": "Gave piano to player {0}!",
  "lowfuel": "You're low on fuel !!",
  "nopianos": "You have no Pianos",
  "currpianos": "Current Pianos : {0}",
  "giveusage": "You need to supply a valid SteamId."
}
```
## Known Issues
1. Lantern can be started or stopped by another player, which can cause the lantern cycle to be out of sync (off while flying).

